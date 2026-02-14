// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AgentLearn.Services.Executors;

/// <summary>
/// Fan-in executor for concurrent mode that accumulates results from parallel pipelines.
/// When all expected inputs are received, formats a selection prompt and sends it downstream.
/// </summary>
public sealed class JokeAggregatorExecutor(string id, int expectedInputs, ILogger logger)
    : Executor(id), IResettableExecutor
{
    private readonly Lock _lock = new();
    private readonly List<List<ChatMessage>> _accumulated = [];
    private int _receivedCount;

    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder) =>
        routeBuilder
            .AddHandler<List<ChatMessage>>(HandleChatMessagesAsync)
            .AddHandler<TurnToken>(HandleTurnTokenAsync);

    private async ValueTask HandleChatMessagesAsync(
        List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
    {
        bool allReceived;
        List<List<ChatMessage>>? snapshot = null;
        int currentCount;

        lock (_lock)
        {
            _accumulated.Add(messages);
            _receivedCount++;
            currentCount = _receivedCount;
            allReceived = _receivedCount >= expectedInputs;
            if (allReceived)
            {
                snapshot = [.. _accumulated];
            }
        }

        logger.LogDebug("Executor '{Id}' received pipeline result — {Current}/{Expected}", Id, currentCount, expectedInputs);

        if (allReceived && snapshot is not null)
        {
            string jokesForSelection = string.Join("\n\n", snapshot.Select((msgs, i) =>
            {
                ChatMessage? lastAssistant = msgs.LastOrDefault(m => m.Role == ChatRole.Assistant);
                string authorName = lastAssistant?.AuthorName ?? $"Pipeline {i + 1}";
                string text = lastAssistant?.Text?.Trim() ?? "(no joke)";
                return $"Joke {i + 1} ({authorName}):\n{text}";
            }));

            string prompt = $"Please select the best joke from the following options:\n\n{jokesForSelection}";

            logger.LogDebug("Executor '{Id}' sending selection prompt — all pipelines complete", Id);
            await context.SendMessageAsync(
                new ChatMessage(ChatRole.User, prompt), cancellationToken: cancellationToken);
            await context.SendMessageAsync(
                new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
        }
    }

    private static ValueTask HandleTurnTokenAsync(
        TurnToken token, IWorkflowContext context, CancellationToken cancellationToken) =>
        default;

    public ValueTask ResetAsync()
    {
        lock (_lock)
        {
            _accumulated.Clear();
            _receivedCount = 0;
        }

        return default;
    }
}
