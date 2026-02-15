using System.ComponentModel;
using AgentLearn.Extensions;
using AgentLearn.Models;
using AgentLearn.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AgentLearn.TaskKinds.StoryGenerator;

// ── Models ──────────────────────────────────────────────────────────────────

/// <summary>
/// Typed output produced by the story workflow, containing the generated story text.
/// </summary>
public record StoryOutput(string Story)
{
    /// <inheritdoc />
    public override string ToString() => this.Story;
}

// ── Executors ───────────────────────────────────────────────────────────────

/// <summary>
/// Bridge executor that receives a character name string (from the HITL RequestPort response)
/// and forwards it as a ChatMessage + TurnToken to the downstream storyteller agent.
/// </summary>
public sealed class StoryNameBridgeExecutor(string id, ILogger logger)
    : Executor(id, declareCrossRunShareable: true), IResettableExecutor
{
    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder) =>
        routeBuilder
            .AddHandler<string>(HandleNameAsync)
            .AddHandler<List<ChatMessage>>(HandleChatMessagesAsync)
            .AddHandler<TurnToken>(HandleTurnTokenAsync);

    private async ValueTask HandleNameAsync(
        string name, IWorkflowContext context, CancellationToken cancellationToken)
    {
        logger.LogDebug("Executor '{Id}' received character name: '{Name}'", Id, name);
        await context.SendMessageAsync(
            new ChatMessage(ChatRole.User, $"Write a punchy 2-sentence story about {name}"),
            cancellationToken: cancellationToken);
        await context.SendMessageAsync(
            new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
    }

    private ValueTask HandleChatMessagesAsync(
        List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken) =>
        context.SendMessageAsync(messages, cancellationToken: cancellationToken);

    private ValueTask HandleTurnTokenAsync(
        TurnToken token, IWorkflowContext context, CancellationToken cancellationToken) =>
        context.SendMessageAsync(token, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public ValueTask ResetAsync() => default;
}

/// <summary>
/// Typed output executor that yields both <see cref="StoryOutput"/> (for typed consumers)
/// and <see cref="List{ChatMessage}"/> (for DevUI chat consumers).
/// </summary>
public sealed class StoryOutputExecutor(string id, ILogger logger)
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
            return;
        }

        string storyText = lastAssistant.Text?.Trim() ?? string.Empty;

        logger.LogDebug("Executor '{Id}' yielding output — story: {Summary}", Id, Summarize(storyText));
        await context.YieldOutputAsync(new StoryOutput(storyText), cancellationToken: cancellationToken);
        await context.YieldOutputAsync(messages, cancellationToken: cancellationToken);
    }

    private static ValueTask HandleTurnTokenAsync(
        TurnToken token, IWorkflowContext context, CancellationToken cancellationToken) =>
        default;

    /// <inheritdoc />
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

// ── Agents & Workflows ──────────────────────────────────────────────────────

/// <summary>
/// Factory methods for creating story agents and workflows.
/// </summary>
public static class StoryAgents
{
    private const string StorytellerInstructions = """
        You are a creative storyteller. When given a character name, write a punchy 2-sentence story about them.
        Always use the GetLuckyNumber tool to get a lucky number, and incorporate that number into your story.
        """;

    /// <summary>
    /// Creates a storyteller agent that writes short stories using a lucky-number tool.
    /// </summary>
    public static AIAgent CreateStoryteller(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return new ChatClientAgent(
            chatClient,
            name: "Storyteller",
            instructions: StorytellerInstructions,
            tools: [AIFunctionFactory.Create(GetLuckyNumber)])
            .AsBuilder()
            .UseToolInvocationLogging(loggerFactory)
            .Build();
    }

    /// <summary>
    /// Builds the HITL story workflow: RequestPort → NameBridge → Storyteller → StoryOutput.
    /// </summary>
    public static Workflow BuildSingleWorkflow(AIAgent storyteller, ILoggerFactory loggerFactory)
    {
        AIAgentHostOptions hostOptions = new()
        {
            ReassignOtherAgentsAsUsers = true,
            ForwardIncomingMessages = true,
            EmitAgentResponseEvents = true,
        };

        RequestPort<string, string> namePort = RequestPort.Create<string, string>("AskCharacterName");

        StoryNameBridgeExecutor bridgeExec = new("NameBridge", loggerFactory.CreateLogger<StoryNameBridgeExecutor>());
        StoryOutputExecutor outputExec = new("StoryOutput", loggerFactory.CreateLogger<StoryOutputExecutor>());

        ExecutorBinding bridgeBinding = bridgeExec.BindExecutor();
        ExecutorBinding storytellerBinding = storyteller.BindAsExecutor(hostOptions);
        ExecutorBinding outputBinding = outputExec.BindExecutor();

        return new WorkflowBuilder(namePort)
            .AddEdge(namePort, bridgeBinding)
            .AddEdge(bridgeBinding, storytellerBinding)
            .AddEdge(storytellerBinding, outputBinding)
            .WithOutputFrom(outputBinding)
            .WithName("StoryWorkflow")
            .Build();
    }

    [Description("Gets a random lucky number between 1 and 1000.")]
    private static int GetLuckyNumber()
    {
        return Random.Shared.Next(1, 1000);
    }
}

// ── Handler ─────────────────────────────────────────────────────────────────

/// <summary>
/// Handles StoryGenerator task requests by running the HITL story workflow.
/// </summary>
public class StoryGeneratorHandler(IServiceProvider serviceProvider, ILogger<StoryGeneratorHandler> logger) : ITaskHandler
{
    /// <inheritdoc />
    public TaskKind Kind => TaskKind.StoryGenerator;

    /// <inheritdoc />
    public async Task<string> HandleAsync(TaskRequest request)
    {
        if (request.Mode != AgentMode.Single)
        {
            throw new NotSupportedException($"StoryGenerator only supports Single mode, got '{request.Mode}'.");
        }

        Workflow workflow = serviceProvider.GetRequiredKeyedService<Workflow>("StoryWorkflow");
        StoryOutput result = await ExecuteWorkflowAsync(workflow);

        logger.LogDebug("StoryWorkflow finished — result: '{Result}'", result.Story);
        return result.ToString();
    }

    private async Task<StoryOutput> ExecuteWorkflowAsync(Workflow workflow)
    {
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, "What is the character's name?");

        StoryOutput? result = null;
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInfo:
                    Console.Write("Enter character name: ");
                    string characterName = Console.ReadLine()?.Trim() ?? "Unknown";
                    logger.LogDebug("HITL request received — providing character name: '{Name}'", characterName);
                    await run.SendResponseAsync(requestInfo.Request.CreateResponse(characterName));
                    break;

                case AgentResponseEvent response:
                    string responseText = response.Response.Messages
                        .LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? "";
                    logger.LogDebug("[AgentResponseEvent] Agent '{Agent}' responded — output: {Output}",
                        response.ExecutorId, Summarize(responseText));
                    break;

                case WorkflowOutputEvent output:
                    StoryOutput? storyOutput = output.As<StoryOutput>();
                    if (storyOutput is not null)
                    {
                        result = storyOutput;
                    }

                    break;
            }
        }

        return result ?? new StoryOutput("No story was generated.");
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

// ── DI Registration ─────────────────────────────────────────────────────────

/// <summary>
/// Extension methods for registering story agents and workflows with the DI container.
/// </summary>
public static class StoryServiceRegistration
{
    /// <summary>
    /// Registers the Storyteller AI agent as a keyed singleton.
    /// </summary>
    public static WebApplicationBuilder AddStoryAgents(this WebApplicationBuilder builder)
    {
        builder.AddAIAgent("Storyteller", (sp, key) =>
            StoryAgents.CreateStoryteller(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<ILoggerFactory>()));

        return builder;
    }

    /// <summary>
    /// Registers the StoryWorkflow with DevUI support.
    /// </summary>
    public static WebApplicationBuilder AddStoryWorkflows(this WebApplicationBuilder builder)
    {
        builder.AddWorkflow("StoryWorkflow", (sp, key) =>
        {
            AIAgent storyteller = sp.GetRequiredKeyedService<AIAgent>("Storyteller");
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return StoryAgents.BuildSingleWorkflow(storyteller, loggerFactory);
        }).AddAsAIAgent();

        return builder;
    }
}
