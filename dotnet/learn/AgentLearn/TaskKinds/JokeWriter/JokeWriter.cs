using System.ComponentModel;
using AgentLearn.Extensions;
using AgentLearn.Models;
using AgentLearn.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AgentLearn.TaskKinds.JokeWriter;

// ── Models ──────────────────────────────────────────────────────────────────

/// <summary>
/// Typed input for joke workflows.
/// </summary>
/// <param name="Topic">The topic to write a joke about.</param>
public record JokeRequest(string Topic);

/// <summary>
/// Typed output produced by joke workflows.
/// </summary>
/// <param name="Joke">The generated joke text.</param>
public record JokeOutput(string Joke)
{
    /// <inheritdoc />
    public override string ToString() => this.Joke;
}

// ── Executors ───────────────────────────────────────────────────────────────

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

    /// <inheritdoc />
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

// ── Agents & Workflows ──────────────────────────────────────────────────────

/// <summary>
/// Factory methods for creating joke agents and building joke workflows.
/// </summary>
public static class JokeAgents
{
    private const string OTelSourceName = "AgentLearn.Agents";

    private const string WriterInstructions = """
        You are a comedian who writes funny jokes. When given a topic, write a short, clever joke about it.
        Always use the GetDayOfYear tool to find out how many days into the year we are,
        and incorporate that number into your joke.
        """;

    private const string CriticInstructions = """
        You are a comedy critic. Analyze the joke provided and give constructive criticism.
        Point out what works, what doesn't, and suggest specific improvements.
        Be concise but helpful.
        """;

    private const string EditorInstructions = """
        You are a comedy editor. Given a joke and criticism of that joke,
        rewrite the joke to address the criticism and make it funnier.
        Output only the final edited joke, nothing else.
        """;

    private const string NeutralWriterInstructions = """
        You are a comedian who writes balanced, universally appealing jokes.
        Your humor is observational and relatable to everyone regardless of their background.
        When given a topic, write a short, clever joke about it.
        Always use the GetDayOfYear tool to find out how many days into the year we are,
        and incorporate that number into your joke.
        """;

    private const string LiberalWriterInstructions = """
        You are a progressive comedian who writes jokes with a liberal perspective.
        Your humor often touches on social justice, environmentalism, and progressive values.
        When given a topic, write a short, clever joke about it from this perspective.
        Always use the GetDayOfYear tool to find out how many days into the year we are,
        and incorporate that number into your joke.
        """;

    private const string ConservativeWriterInstructions = """
        You are a traditional comedian who writes jokes with a conservative perspective.
        Your humor often touches on traditional values, personal responsibility, and patriotism.
        When given a topic, write a short, clever joke about it from this perspective.
        Always use the GetDayOfYear tool to find out how many days into the year we are,
        and incorporate that number into your joke.
        """;

    private const string SelectorInstructions = """
        You are a comedy expert who selects the best joke from a set of options.
        Evaluate each joke based on:
        - Humor and cleverness
        - Universal appeal
        - Originality
        - How well it incorporates the day of year

        Output ONLY the winning joke text, nothing else. Do not add any commentary or explanation.
        """;

    /// <summary>Creates a Writer agent with the GetDayOfYear tool.</summary>
    public static AIAgent CreateWriter(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return new ChatClientAgent(
            chatClient,
            name: "JokeWriter",
            instructions: WriterInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseToolInvocationLogging(loggerFactory)
            .Build();
    }

    /// <summary>Creates a Critic agent that reviews jokes.</summary>
    public static AIAgent CreateCritic(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "JokeCritic",
            instructions: CriticInstructions);
    }

    /// <summary>Creates an Editor agent that rewrites jokes based on criticism.</summary>
    public static AIAgent CreateEditor(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "JokeEditor",
            instructions: EditorInstructions);
    }

    /// <summary>Creates a neutral-perspective Writer agent.</summary>
    public static AIAgent CreateNeutralWriter(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return new ChatClientAgent(
            chatClient,
            name: "JokeNeutralWriter",
            instructions: NeutralWriterInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseToolInvocationLogging(loggerFactory)
            .UseOpenTelemetry(OTelSourceName)
            .Build();
    }

    /// <summary>Creates a liberal-perspective Writer agent.</summary>
    public static AIAgent CreateLiberalWriter(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return new ChatClientAgent(
            chatClient,
            name: "JokeLiberalWriter",
            instructions: LiberalWriterInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseToolInvocationLogging(loggerFactory)
            .UseOpenTelemetry(OTelSourceName)
            .Build();
    }

    /// <summary>Creates a conservative-perspective Writer agent.</summary>
    public static AIAgent CreateConservativeWriter(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return new ChatClientAgent(
            chatClient,
            name: "JokeConservativeWriter",
            instructions: ConservativeWriterInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseToolInvocationLogging(loggerFactory)
            .UseOpenTelemetry(OTelSourceName)
            .Build();
    }

    /// <summary>Creates a Selector agent that picks the best joke from candidates.</summary>
    public static AIAgent CreateSelector(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "JokeSelector",
            instructions: SelectorInstructions)
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();
    }

    /// <summary>
    /// Creates a sequential workflow (Writer → Critic → Editor) as a sub-workflow.
    /// Use <see cref="Workflow.BindAsExecutor"/> to compose into a parent workflow.
    /// </summary>
    public static Workflow CreateWriterPipeline(
        IChatClient chatClient,
        string name,
        string writerInstructions,
        ILoggerFactory loggerFactory)
    {
        AIAgent writer = new ChatClientAgent(
            chatClient,
            name: $"Joke{name}Writer",
            instructions: writerInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseToolInvocationLogging(loggerFactory)
            .UseOpenTelemetry(OTelSourceName)
            .Build();

        AIAgent critic = new ChatClientAgent(
            chatClient,
            name: $"Joke{name}Critic",
            instructions: CriticInstructions)
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();

        AIAgent editor = new ChatClientAgent(
            chatClient,
            name: $"Joke{name}Editor",
            instructions: EditorInstructions)
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();

        return AgentWorkflowBuilder.BuildSequential([writer, critic, editor]);
    }

    /// <summary>
    /// Creates a Neutral perspective writer pipeline (Writer → Critic → Editor).
    /// </summary>
    public static Workflow CreateNeutralPipeline(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return CreateWriterPipeline(chatClient, "Neutral", NeutralWriterInstructions, loggerFactory);
    }

    /// <summary>
    /// Creates a Liberal perspective writer pipeline (Writer → Critic → Editor).
    /// </summary>
    public static Workflow CreateLiberalPipeline(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return CreateWriterPipeline(chatClient, "Liberal", LiberalWriterInstructions, loggerFactory);
    }

    /// <summary>
    /// Creates a Conservative perspective writer pipeline (Writer → Critic → Editor).
    /// </summary>
    public static Workflow CreateConservativePipeline(IChatClient chatClient, ILoggerFactory loggerFactory)
    {
        return CreateWriterPipeline(chatClient, "Conservative", ConservativeWriterInstructions, loggerFactory);
    }

    /// <summary>
    /// Builds a single-agent typed workflow: JokeInput → Writer → JokeOutput.
    /// </summary>
    public static Workflow BuildSingleWorkflow(AIAgent writer, ILoggerFactory loggerFactory)
    {
        AIAgentHostOptions hostOptions = new()
        {
            ReassignOtherAgentsAsUsers = true,
            ForwardIncomingMessages = true,
            EmitAgentResponseEvents = true,
        };

        JokeInputExecutor inputExecutor = new("SingleInput", loggerFactory.CreateLogger<JokeInputExecutor>());
        JokeOutputExecutor outputExecutor = new("SingleOutput", loggerFactory.CreateLogger<JokeOutputExecutor>());

        ExecutorBinding input = inputExecutor.BindExecutor();
        ExecutorBinding writerBinding = writer.BindAsExecutor(hostOptions);
        ExecutorBinding output = outputExecutor.BindExecutor();

        return new WorkflowBuilder(input)
            .AddEdge(input, writerBinding)
            .AddEdge(writerBinding, output)
            .WithOutputFrom(output)
            .WithName("JokeSingleWorkflow")
            .Build();
    }

    /// <summary>
    /// Builds a sequential typed workflow: JokeInput → Writer → Critic → Editor → JokeOutput.
    /// </summary>
    public static Workflow BuildSequentialWorkflow(AIAgent writer, AIAgent critic, AIAgent editor, ILoggerFactory loggerFactory)
    {
        AIAgentHostOptions hostOptions = new()
        {
            ReassignOtherAgentsAsUsers = true,
            ForwardIncomingMessages = true,
            EmitAgentResponseEvents = true,
        };

        JokeInputExecutor inputExecutor = new("SeqInput", loggerFactory.CreateLogger<JokeInputExecutor>());
        JokeOutputExecutor outputExecutor = new("SeqOutput", loggerFactory.CreateLogger<JokeOutputExecutor>());

        ExecutorBinding input = inputExecutor.BindExecutor();
        ExecutorBinding writerBinding = writer.BindAsExecutor(hostOptions);
        ExecutorBinding criticBinding = critic.BindAsExecutor(hostOptions);
        ExecutorBinding editorBinding = editor.BindAsExecutor(hostOptions);
        ExecutorBinding output = outputExecutor.BindExecutor();

        return new WorkflowBuilder(input)
            .AddEdge(input, writerBinding)
            .AddEdge(writerBinding, criticBinding)
            .AddEdge(criticBinding, editorBinding)
            .AddEdge(editorBinding, output)
            .WithOutputFrom(output)
            .WithName("JokeSequentialWorkflow")
            .Build();
    }

    /// <summary>
    /// Builds a concurrent typed workflow:
    /// JokeInput → FanOut[Sub-Workflows] → Aggregator → Selector → JokeOutput.
    /// Pipelines are bound directly as sub-workflows (not wrapped as agents).
    /// </summary>
    public static Workflow BuildConcurrentWorkflow(Workflow[] pipelines, AIAgent selector, ILoggerFactory loggerFactory)
    {
        AIAgentHostOptions hostOptions = new()
        {
            ReassignOtherAgentsAsUsers = true,
            ForwardIncomingMessages = true,
            EmitAgentResponseEvents = true,
        };

        JokeInputExecutor inputExecutor = new("ConcInput", loggerFactory.CreateLogger<JokeInputExecutor>());
        JokeOutputExecutor outputExecutor = new("ConcOutput", loggerFactory.CreateLogger<JokeOutputExecutor>());
        JokeAggregatorExecutor aggregator = new("JokeAggregator", pipelines.Length, loggerFactory.CreateLogger<JokeAggregatorExecutor>());

        ExecutorBinding input = inputExecutor.BindExecutor();
        ExecutorBinding output = outputExecutor.BindExecutor();
        ExecutorBinding agg = aggregator.BindExecutor();
        ExecutorBinding selectorBinding = selector.BindAsExecutor(hostOptions);

        ExecutorBinding[] pipelineBindings = pipelines
            .Select((p, i) => p.BindAsExecutor($"Pipeline{i}"))
            .ToArray();

        WorkflowBuilder builder = new(input);
        builder.AddFanOutEdge(input, pipelineBindings);
        builder.AddFanInEdge(pipelineBindings, agg);
        builder.AddEdge(agg, selectorBinding);
        builder.AddEdge(selectorBinding, output);
        builder.WithOutputFrom(output);
        builder.WithName("JokeConcurrentWorkflow");

        return builder.Build();
    }

    [Description("Gets how many days into the current year we are (1-366).")]
    private static int GetDayOfYear()
    {
        return DateTime.Now.DayOfYear;
    }
}

// ── Handler ─────────────────────────────────────────────────────────────────

/// <summary>
/// Handles joke-writing tasks by dispatching to the appropriate workflow mode.
/// </summary>
public class JokeWriterHandler(IServiceProvider serviceProvider, ILogger<JokeWriterHandler> logger) : ITaskHandler
{
    /// <inheritdoc />
    public TaskKind Kind => TaskKind.JokeWriter;

    /// <inheritdoc />
    public async Task<string> HandleAsync(TaskRequest request)
    {
        string workflowKey = request.Mode switch
        {
            AgentMode.Single => "JokeSingleWorkflow",
            AgentMode.Sequential => "JokeSequentialWorkflow",
            AgentMode.Concurrent => "JokeConcurrentWorkflow",
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

// ── DI Registration ─────────────────────────────────────────────────────────

/// <summary>
/// Extension methods for registering joke agents and workflows with the DI container.
/// </summary>
public static class JokeServiceRegistration
{
    /// <summary>
    /// Registers the Writer, Critic, Editor, and Selector AI agents as keyed singletons.
    /// </summary>
    public static WebApplicationBuilder AddJokeAgents(this WebApplicationBuilder builder)
    {
        builder.AddAIAgent("JokeWriter", (sp, key) =>
            JokeAgents.CreateWriter(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<ILoggerFactory>()));

        builder.AddAIAgent("JokeCritic", (sp, key) =>
            JokeAgents.CreateCritic(sp.GetRequiredService<IChatClient>()));

        builder.AddAIAgent("JokeEditor", (sp, key) =>
            JokeAgents.CreateEditor(sp.GetRequiredService<IChatClient>()));

        builder.AddAIAgent("JokeSelector", (sp, key) =>
            JokeAgents.CreateSelector(sp.GetRequiredService<IChatClient>()));

        return builder;
    }

    /// <summary>
    /// Registers the Single, Sequential, and Concurrent joke workflows with DevUI support.
    /// </summary>
    public static WebApplicationBuilder AddJokeWorkflows(this WebApplicationBuilder builder)
    {
        // Single: JokeInput → Writer → JokeOutput
        builder.AddWorkflow("JokeSingleWorkflow", (sp, key) =>
        {
            AIAgent writer = sp.GetRequiredKeyedService<AIAgent>("JokeWriter");
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return JokeAgents.BuildSingleWorkflow(writer, loggerFactory);
        }).AddAsAIAgent();

        // Sequential: JokeInput → Writer → Critic → Editor → JokeOutput
        builder.AddWorkflow("JokeSequentialWorkflow", (sp, key) =>
        {
            AIAgent writer = sp.GetRequiredKeyedService<AIAgent>("JokeWriter");
            AIAgent critic = sp.GetRequiredKeyedService<AIAgent>("JokeCritic");
            AIAgent editor = sp.GetRequiredKeyedService<AIAgent>("JokeEditor");
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return JokeAgents.BuildSequentialWorkflow(writer, critic, editor, loggerFactory);
        }).AddAsAIAgent();

        // Concurrent: JokeInput → FanOut[Pipelines] → Aggregator → Selector → JokeOutput
        builder.AddWorkflow("JokeConcurrentWorkflow", (sp, key) =>
        {
            IChatClient client = sp.GetRequiredService<IChatClient>();
            AIAgent selector = sp.GetRequiredKeyedService<AIAgent>("JokeSelector");
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            Workflow[] pipelines =
            [
                JokeAgents.CreateNeutralPipeline(client, loggerFactory),
                JokeAgents.CreateLiberalPipeline(client, loggerFactory),
                JokeAgents.CreateConservativePipeline(client, loggerFactory),
            ];
            return JokeAgents.BuildConcurrentWorkflow(pipelines, selector, loggerFactory);
        }).AddAsAIAgent();

        return builder;
    }
}
