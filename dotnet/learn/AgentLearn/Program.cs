using System.Text.Json.Serialization;
using AgentLearn.Models;
using AgentLearn.Services;
using AgentLearn.TaskKinds.JokeWriter;
using AgentLearn.TaskKinds.StoryGenerator;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Emit distributed traces to Jaeger (localhost:4317) so devs can visualise
// agent call chains, LLM latencies, and HTTP request flow in the Jaeger UI.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("AgentLearn"))
    .WithTracing(tracing => tracing
        .AddSource("AgentLearn.AI")
        .AddSource("AgentLearn.Agents")
        .AddSource("Microsoft.Extensions.AI")
        .AddSource("Microsoft.Agents.AI")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));

// Enable MVC controllers (TaskController) and serialize enums as strings
// so API responses return "Single" instead of 0 in JSON payloads.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Expose the OpenAPI spec at /openapi/v1.json (dev only, mapped below).
builder.Services.AddOpenApi();

// Register the shared IChatClient used by every agent. The provider (GitHub Models,
// OpenAI, Azure OpenAI, Anthropic) is chosen at runtime from appsettings "Llm" section.
builder.Services.AddSingleton<IChatClient>(sp =>
{
    IConfiguration config = sp.GetRequiredService<IConfiguration>();
    IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();

    LlmOptions llmOptions = new();
    config.GetSection("Llm").Bind(llmOptions);

    return ChatClientFactory.Create(llmOptions, env);
});

// Wires the /task endpoint handlers that translate HTTP requests into agent runs.
builder.Services.AddSingleton<ITaskHandler, JokeWriterHandler>();
builder.Services.AddSingleton<ITaskHandler, StoryGeneratorHandler>();

// Register Writer/Critic/Editor/Selector agents as keyed singletons so they
// appear in DevUI and can be resolved individually by workflow factories.
builder.AddJokeAgents();

// Register Single, Sequential, and Concurrent workflows (each also exposed as an
// AIAgent via AddAsAIAgent) so DevUI lists them and the task API can invoke them.
builder.AddJokeWorkflows();

// Register Storyteller agent and HITL StoryWorkflow (also exposed as AIAgent for DevUI).
builder.AddStoryAgents();
builder.AddStoryWorkflows();

// Add the DevUI dashboard (dev only) — browse to /devui to chat with any
// registered agent/workflow and inspect their execution traces interactively.
if (builder.Environment.IsDevelopment())
{
    builder.AddDevUI();
}

// Expose OpenAI-compatible /responses and /conversations endpoints so external
// clients (curl, SDKs, ChatGPT-compatible UIs) can talk to any registered agent.
builder.AddOpenAIResponses();
builder.AddOpenAIConversations();

WebApplication app = builder.Build();

// Dev-only middleware: serves the OpenAPI spec at /openapi/v1.json and the
// DevUI dashboard at /devui for interactive agent testing and trace inspection.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDevUI();
}

// Wire up OpenAI-compatible REST routes (/responses, /conversations) so any
// OpenAI SDK or ChatGPT-compatible client can call the registered agents.
app.MapOpenAIResponses();
app.MapOpenAIConversations();

// Force HTTPS and route attribute-based controllers (e.g. TaskController at /task).
app.UseHttpsRedirection();
app.MapControllers();

app.Run();
