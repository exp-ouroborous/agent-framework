// Copyright (c) Microsoft. All rights reserved.

using AgentLearn.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AgentLearn.Services.Executors;

/// <summary>
/// Dual-protocol start executor that handles both typed <see cref="JokeRequest"/> input
/// and ChatProtocol (<see cref="List{ChatMessage}"/> + <see cref="TurnToken"/>) for DevUI.
/// </summary>
public sealed class JokeInputExecutor(string id, ILogger logger)
    : Executor(id, declareCrossRunShareable: true), IResettableExecutor
{
    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder) =>
        routeBuilder
            .AddHandler<JokeRequest>(HandleJokeRequestAsync)
            .AddHandler<List<ChatMessage>>(HandleChatMessagesAsync)
            .AddHandler<ChatMessage>(HandleChatMessageAsync)
            .AddHandler<TurnToken>(HandleTurnTokenAsync);

    private async ValueTask HandleJokeRequestAsync(
        JokeRequest request, IWorkflowContext context, CancellationToken cancellationToken)
    {
        logger.LogDebug("Executor '{Id}' received JokeRequest — topic: '{Topic}'", Id, request.Topic);
        await context.SendMessageAsync(
            new ChatMessage(ChatRole.User, request.Topic), cancellationToken: cancellationToken);
        await context.SendMessageAsync(
            new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
    }

    private ValueTask HandleChatMessagesAsync(
        List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
    {
        ChatMessage? last = messages.LastOrDefault();
        string preview = last is not null ? $"last={last.Role}: {Summarize(last.Text ?? "")}" : "empty";
        logger.LogDebug("Executor '{Id}' received List<ChatMessage> — {Count} messages, {Preview}",
            Id, messages.Count, preview);
        return context.SendMessageAsync(messages, cancellationToken: cancellationToken);
    }

    private ValueTask HandleChatMessageAsync(
        ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken) =>
        context.SendMessageAsync(message, cancellationToken: cancellationToken);

    private ValueTask HandleTurnTokenAsync(
        TurnToken token, IWorkflowContext context, CancellationToken cancellationToken) =>
        context.SendMessageAsync(token, cancellationToken: cancellationToken);

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
