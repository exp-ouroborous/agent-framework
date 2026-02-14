// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;
using AgentLearn.Models;
using AgentLearn.Services;
using AgentLearn.Services.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("AgentLearn"))
    .WithTracing(tracing => tracing
        .AddSource("AgentLearn.AI") // AI chat client telemetry
        .AddSource("AgentLearn.Agents") // Custom agent telemetry
        .AddSource("Microsoft.Extensions.AI") // Microsoft.Extensions.AI telemetry
        .AddSource("Microsoft.Agents.AI") // MS Agent Framework telemetry
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();

// Configure AI Chat Client from configuration
// Supports GitHub Models, OpenAI, Azure OpenAI, and Anthropic providers
builder.Services.AddSingleton<IChatClient>(sp =>
{
    IConfiguration config = sp.GetRequiredService<IConfiguration>();
    IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();

    LlmOptions llmOptions = new();
    config.GetSection("Llm").Bind(llmOptions);

    return ChatClientFactory.Create(llmOptions, env);
});

builder.Services.AddSingleton<ITaskHandler, JokeWriterHandler>();

// Register agents using the framework's hosting pattern.
// AddAIAgent registers as keyed singletons, auto-discovered by DevUI.
builder.AddAIAgent("Writer", (sp, key) =>
    JokeAgents.CreateWriter(sp.GetRequiredService<IChatClient>()));

builder.AddAIAgent("Critic", (sp, key) =>
    JokeAgents.CreateCritic(sp.GetRequiredService<IChatClient>()));

builder.AddAIAgent("Editor", (sp, key) =>
    JokeAgents.CreateEditor(sp.GetRequiredService<IChatClient>()));

builder.AddAIAgent("Selector", (sp, key) =>
    JokeAgents.CreateSelector(sp.GetRequiredService<IChatClient>()));

// Register typed workflows with dual-protocol support (typed input + DevUI chat).
// Single: JokeInput → Writer → JokeOutput
builder.AddWorkflow("SingleWorkflow", (sp, key) =>
{
    AIAgent writer = sp.GetRequiredKeyedService<AIAgent>("Writer");
    ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return JokeAgents.BuildSingleWorkflow(writer, loggerFactory);
}).AddAsAIAgent();

// Sequential: JokeInput → Writer → Critic → Editor → JokeOutput
builder.AddWorkflow("SequentialWorkflow", (sp, key) =>
{
    AIAgent writer = sp.GetRequiredKeyedService<AIAgent>("Writer");
    AIAgent critic = sp.GetRequiredKeyedService<AIAgent>("Critic");
    AIAgent editor = sp.GetRequiredKeyedService<AIAgent>("Editor");
    ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return JokeAgents.BuildSequentialWorkflow(writer, critic, editor, loggerFactory);
}).AddAsAIAgent();

// Concurrent: JokeInput → FanOut[Pipelines] → Aggregator → Selector → JokeOutput
builder.AddWorkflow("ConcurrentWorkflow", (sp, key) =>
{
    IChatClient client = sp.GetRequiredService<IChatClient>();
    AIAgent selector = sp.GetRequiredKeyedService<AIAgent>("Selector");
    ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    Workflow[] pipelines =
    [
        JokeAgents.CreateNeutralPipeline(client),
        JokeAgents.CreateLiberalPipeline(client),
        JokeAgents.CreateConservativePipeline(client),
    ];
    return JokeAgents.BuildConcurrentWorkflow(pipelines, selector, loggerFactory);
}).AddAsAIAgent();

// DevUI and hosting services
if (builder.Environment.IsDevelopment())
{
    builder.AddDevUI();
}

builder.AddOpenAIResponses();
builder.AddOpenAIConversations();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDevUI();
}

app.MapOpenAIResponses();
app.MapOpenAIConversations();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
