// Copyright (c) Microsoft. All rights reserved.

using AgentLearn.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AgentLearn.Services.Executors;

/// <summary>
/// Typed output executor that yields both <see cref="JokeOutput"/> (for typed consumers)
/// and <see cref="List{ChatMessage}"/> (for DevUI chat consumers).
/// </summary>
public sealed class JokeOutputExecutor(string id, ILogger logger)
    : Executor(id, declareCrossRunShareable: true), IResettableExecutor
{
    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder) =>
        routeBuilder
            .AddHandler<List<ChatMessage>>(HandleChatMessagesAsync)
            .AddHandler<TurnToken>(HandleTurnTokenAsync);

    private async ValueTask HandleChatMessagesAsync(
        List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
    {
        ChatMessage? lastAssistant = messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (lastAssistant is null)
        {
            // Forwarded input messages (no assistant response yet) — skip.
            return;
        }

        string jokeText = lastAssistant.Text?.Trim() ?? string.Empty;

        logger.LogDebug("Executor '{Id}' yielding output — joke: {Summary}", Id, Summarize(jokeText));
        await context.YieldOutputAsync(new JokeOutput(jokeText), cancellationToken: cancellationToken);
        await context.YieldOutputAsync(messages, cancellationToken: cancellationToken);
    }

    private static ValueTask HandleTurnTokenAsync(
        TurnToken token, IWorkflowContext context, CancellationToken cancellationToken) =>
        default;

    public ValueTask ResetAsync() => default;

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
