// Copyright (c) Microsoft. All rights reserved.

using AgentLearn.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentLearn.Services;

public class JokeWriterHandler(IServiceProvider serviceProvider, ILogger<JokeWriterHandler> logger) : ITaskHandler
{
    public TaskKind Kind => TaskKind.JokeWriter;

    public async Task<string> HandleAsync(TaskRequest request)
    {
        string workflowKey = request.Mode switch
        {
            AgentMode.Single => "SingleWorkflow",
            AgentMode.Sequential => "SequentialWorkflow",
            AgentMode.Concurrent => "ConcurrentWorkflow",
            _ => throw new NotSupportedException($"Mode '{request.Mode}' is not supported yet.")
        };

        logger.LogDebug("Workflow '{Key}' started ({Mode}) — topic: '{Topic}'",
            workflowKey, request.Mode, request.Task);

        Workflow workflow = serviceProvider.GetRequiredKeyedService<Workflow>(workflowKey);
        JokeOutput result = await ExecuteWorkflowAsync(workflow, request.Task);

        logger.LogDebug("Workflow '{Key}' finished — result: '{Result}'", workflowKey, result.Joke);
        return result.ToString();
    }

    private async Task<JokeOutput> ExecuteWorkflowAsync(Workflow workflow, string topic)
    {
        JokeRequest jokeRequest = new(topic);
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, jokeRequest);

        JokeOutput? result = null;
        Dictionary<string, string> pendingInputs = [];
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case ExecutorInvokedEvent invoked:
                    string? inputSummary = invoked.Data switch
                    {
                        ChatMessage msg => $"{msg.Role}: {Summarize(msg.Text ?? "")}",
                        IEnumerable<ChatMessage> msgs => SummarizeMessages(msgs),
                        _ => null,
                    };
                    if (inputSummary is not null)
                    {
                        pendingInputs[invoked.ExecutorId] = inputSummary;
                    }

                    break;

                case AgentResponseEvent response:
                    if (pendingInputs.Remove(response.ExecutorId, out string? pending))
                    {
                        logger.LogDebug("[ExecutorInvokedEvent] Agent '{Agent}' invoked — input: {Summary}",
                            response.ExecutorId, pending);
                    }

                    string responseText = response.Response.Messages
                        .LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? "";
                    logger.LogDebug("[AgentResponseEvent] Agent '{Agent}' responded — output: {Output}",
                        response.ExecutorId, Summarize(responseText));
                    break;

                case WorkflowOutputEvent output:
                    JokeOutput? jokeOutput = output.As<JokeOutput>();
                    if (jokeOutput is not null)
                    {
                        result = jokeOutput;
                    }

                    break;
            }
        }

        return result ?? new JokeOutput("No joke was generated.");
    }

    private static string SummarizeMessages(IEnumerable<ChatMessage> msgs)
    {
        List<ChatMessage> list = msgs as List<ChatMessage> ?? msgs.ToList();
        if (list.Count == 0)
        {
            return "(empty list)";
        }

        ChatMessage last = list[^1];
        return $"{list.Count} messages, last={last.Role}: {Summarize(last.Text ?? "")}";
    }

    private static string Summarize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int lines = text.Count(c => c == '\n') + 1;
        string preview = string.Join(' ', words.Take(8));
        if (words.Length > 8)
        {
            preview += "...";
        }

        return $"\"{preview}\" ({words.Length} words, {lines} lines)";
    }
}
