// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using AgentLearn.Services.Executors;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentLearn.Services.Agents;

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

    public static AIAgent CreateWriter(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "Writer",
            instructions: WriterInstructions);
    }

    public static AIAgent CreateCritic(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "Critic",
            instructions: CriticInstructions);
    }

    public static AIAgent CreateEditor(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "Editor",
            instructions: EditorInstructions);
    }

    public static AIAgent CreateNeutralWriter(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "NeutralWriter",
            instructions: NeutralWriterInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();
    }

    public static AIAgent CreateLiberalWriter(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "LiberalWriter",
            instructions: LiberalWriterInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();
    }

    public static AIAgent CreateConservativeWriter(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "ConservativeWriter",
            instructions: ConservativeWriterInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();
    }

    public static AIAgent CreateSelector(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "Selector",
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
        string writerInstructions)
    {
        AIAgent writer = new ChatClientAgent(
            chatClient,
            name: $"{name}Writer",
            instructions: writerInstructions,
            tools: [AIFunctionFactory.Create(GetDayOfYear)])
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();

        AIAgent critic = new ChatClientAgent(
            chatClient,
            name: $"{name}Critic",
            instructions: CriticInstructions)
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();

        AIAgent editor = new ChatClientAgent(
            chatClient,
            name: $"{name}Editor",
            instructions: EditorInstructions)
            .AsBuilder()
            .UseOpenTelemetry(OTelSourceName)
            .Build();

        return AgentWorkflowBuilder.BuildSequential([writer, critic, editor]);
    }

    /// <summary>
    /// Creates a Neutral perspective writer pipeline (Writer → Critic → Editor).
    /// </summary>
    public static Workflow CreateNeutralPipeline(IChatClient chatClient)
    {
        return CreateWriterPipeline(chatClient, "Neutral", NeutralWriterInstructions);
    }

    /// <summary>
    /// Creates a Liberal perspective writer pipeline (Writer → Critic → Editor).
    /// </summary>
    public static Workflow CreateLiberalPipeline(IChatClient chatClient)
    {
        return CreateWriterPipeline(chatClient, "Liberal", LiberalWriterInstructions);
    }

    /// <summary>
    /// Creates a Conservative perspective writer pipeline (Writer → Critic → Editor).
    /// </summary>
    public static Workflow CreateConservativePipeline(IChatClient chatClient)
    {
        return CreateWriterPipeline(chatClient, "Conservative", ConservativeWriterInstructions);
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
            .WithName("SingleWorkflow")
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
            .WithName("SequentialWorkflow")
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
        builder.WithName("ConcurrentWorkflow");

        return builder.Build();
    }

    [Description("Gets how many days into the current year we are (1-366).")]
    private static int GetDayOfYear()
    {
        return DateTime.Now.DayOfYear;
    }
}
