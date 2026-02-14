# .NET Agentic Capabilities - Deep Dive

**Date:** January 28, 2026

## Executive Summary

The .NET implementation of Microsoft Agent Framework provides enterprise-grade agentic capabilities with:

- **AIAgent abstraction** - Unified interface for all agent types
- **ChatClientAgent** - Primary implementation wrapping Microsoft.Extensions.AI
- **Workflow orchestration** - Graph-based execution with pre-built patterns
- **DurableTask integration** - Reliable, stateful agent orchestrations
- **Human-in-the-Loop** - External events and checkpointing mechanisms

---

## 1. Core Agent Abstractions

### AIAgent

**Location:** `dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs`

Abstract base class for all AI agents:

```csharp
public abstract class AIAgent
{
    public string Id { get; }
    public string? Name { get; }
    public string? Description { get; }

    // Non-streaming invocation
    public Task<AgentResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    // Streaming invocation
    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    // Session management
    public abstract ValueTask<AgentSession> GetNewSessionAsync(
        CancellationToken cancellationToken = default);

    public abstract ValueTask<AgentSession> DeserializeSessionAsync(
        JsonElement serializedSession,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default);
}
```

### ChatClientAgent

**Location:** `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs`

Primary concrete implementation wrapping `IChatClient`:

```csharp
public sealed partial class ChatClientAgent : AIAgent
{
    public ChatClientAgent(
        IChatClient chatClient,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        // Wraps IChatClient with FunctionInvokingChatClient for automatic tool invocation
    }
}
```

**Key Features:**
- Wraps any `IChatClient` implementation (OpenAI, Azure OpenAI, Anthropic, etc.)
- Automatic middleware decoration for function invocation
- Supports custom chat history and context providers
- Configurable via `ChatClientAgentOptions`

### Extension Method

```csharp
// Create agent from any IChatClient
var agent = chatClient.AsAIAgent(
    instructions: "You are a helpful assistant",
    name: "Helper",
    tools: [tool1, tool2]);
```

---

## 2. Tool/Function Calling

### AITool Definition

Tools use the standard `AITool` abstraction from Microsoft.Extensions.AI:

```csharp
public class Tools
{
    private readonly ILogger<Tools> _logger;

    public Tools(ILogger<Tools> logger)
    {
        _logger = logger;
    }

    [Description("Get current weather for a location")]
    public string GetWeather(
        [Description("The location to get weather for")] string location,
        [Description("Temperature unit (celsius/fahrenheit)")] string unit = "celsius")
    {
        return $"Weather in {location}: 72°F";
    }

    [Description("Search for products in the catalog")]
    public async Task<IEnumerable<Product>> SearchProductsAsync(
        [Description("Search query")] string query,
        [Description("Maximum results")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // Async tool execution
        return await _catalogService.SearchAsync(query, limit, cancellationToken);
    }
}
```

### Tool Registration

```csharp
// Create tools from methods
var tools = new Tools(logger);

var agent = chatClient.AsAIAgent(
    instructions: "You are a helpful assistant",
    tools: [
        AIFunctionFactory.Create(tools.GetWeather),
        AIFunctionFactory.Create(tools.SearchProductsAsync),
    ]);
```

### Tool Features

- Automatic function invocation via `FunctionInvokingChatClient` middleware
- Dependency injection support for tool dependencies
- Rich parameter descriptions for LLM understanding
- Async/await support for long-running operations
- Attribute-based metadata (`[Description]`)

---

## 3. Session Management

### AgentSession

**Location:** `dotnet/src/Microsoft.Agents.AI.Abstractions/AgentSession.cs`

```csharp
public abstract class AgentSession
{
    // Serialize session for persistence
    public virtual JsonElement Serialize(
        JsonSerializerOptions? jsonSerializerOptions = null);

    // Service provider pattern
    public virtual object? GetService(
        Type serviceType,
        object? serviceKey = null);
}
```

### ChatClientAgentSession

**Location:** `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgentSession.cs`

```csharp
public sealed class ChatClientAgentSession : AgentSession
{
    // Server-side conversation storage
    public string? ConversationId { get; internal set; }

    // Client-side conversation storage
    public ChatHistoryProvider? ChatHistoryProvider { get; internal set; }

    // Additional context injection
    public AIContextProvider? AIContextProvider { get; internal set; }
}
```

### Session Types

| Type | Description | Use Case |
|------|-------------|----------|
| Service-backed | Chat history managed by AI service | Azure OpenAI Assistants |
| Client-backed | Chat history managed locally | Custom persistence |
| Hybrid | AI context provider adds dynamic context | RAG scenarios |

### ChatHistoryProvider

```csharp
public abstract class ChatHistoryProvider
{
    // Invoked before agent run
    protected internal abstract ValueTask<IEnumerable<ChatMessage>> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default);

    // Invoked after agent run
    protected internal abstract ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default);
}
```

**Built-in implementation:**
- `InMemoryChatHistoryProvider`: Simple in-memory storage

### Usage

```csharp
// Create session
AgentSession session = await agent.GetNewSessionAsync();

// Service-backed
session.ConversationId = "conv_123";

// Client-backed
session.ChatHistoryProvider = new InMemoryChatHistoryProvider();

// Use across multiple invocations
var response1 = await agent.RunAsync(messages1, session);
var response2 = await agent.RunAsync(messages2, session);  // Includes history
```

---

## 4. Workflow Orchestration

### Workflow Architecture

**Location:** `dotnet/src/Microsoft.Agents.AI.Workflows/`

The framework provides graph-based workflow orchestration using a Pregel-inspired "SuperStep" execution model.

### Workflow Class

**Location:** `dotnet/src/Microsoft.Agents.AI.Workflows/Workflow.cs`

```csharp
public class Workflow
{
    // Graph structure
    internal Dictionary<string, ExecutorBinding> ExecutorBindings { get; init; }
    internal Dictionary<string, HashSet<Edge>> Edges { get; init; }

    // Metadata
    public string? Name { get; internal init; }
    public string? Description { get; internal init; }
    public string StartExecutorId { get; }

    // Execution
    public ValueTask<WorkflowRunResult> RunAsync(
        object? input = null,
        WorkflowRunOptions? options = null,
        CancellationToken cancellationToken = default);

    public IAsyncEnumerable<WorkflowEvent> RunStreamingAsync(
        object? input = null,
        WorkflowRunOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

### WorkflowBuilder

```csharp
var builder = new WorkflowBuilder(startExecutor);

// Simple edge
builder.AddEdge(source, target);

// Conditional edge
builder.AddEdge<T>(source, target, condition: input => input.Score > 0.5);

// Fan-out to multiple executors
builder.AddFanOutEdge(source, [target1, target2, target3]);

// Fan-in from multiple executors
builder.AddFanInEdge([source1, source2], target);

var workflow = builder.Build();
```

### Executor

Base class for workflow nodes:

```csharp
public abstract class Executor
{
    // Message handler registration
    protected void Handle<TMessage>(
        Func<TMessage, IWorkflowContext, CancellationToken, ValueTask> handler);

    // Protocol description for external interaction
    public abstract ProtocolDescriptor DescribeProtocol();
}
```

### IWorkflowContext

```csharp
public interface IWorkflowContext
{
    // Send message to connected executors
    ValueTask SendMessageAsync<T>(T message, CancellationToken cancellationToken = default);

    // Yield output from workflow
    ValueTask YieldOutputAsync<T>(T output, CancellationToken cancellationToken = default);

    // Read state
    ValueTask<T?> ReadStateAsync<T>(
        string key,
        string? scopeName = null,
        CancellationToken cancellationToken = default);

    // Update state (queued for next SuperStep)
    ValueTask QueueStateUpdateAsync<T>(
        string key,
        T? value,
        string? scopeName = null,
        CancellationToken cancellationToken = default);
}
```

---

## 5. Pre-Built Workflow Patterns

### AgentWorkflowBuilder

**Location:** `dotnet/src/Microsoft.Agents.AI.Workflows/AgentWorkflowBuilder.cs`

Provides pre-built patterns for common agent orchestrations.

### Sequential Pipeline

Chains agents where output of one feeds into the next:

```csharp
Workflow sequential = AgentWorkflowBuilder.BuildSequential(
    workflowName: "ContentPipeline",
    agents: [researcherAgent, writerAgent, editorAgent]);
```

### Concurrent Execution

Runs agents in parallel, aggregates results:

```csharp
Workflow concurrent = AgentWorkflowBuilder.BuildConcurrent(
    workflowName: "ParallelResearch",
    agents: [agent1, agent2, agent3],
    aggregator: results => results.SelectMany(r => r).ToList());
```

### Handoff-Based Workflows

Agents delegate control to specialists:

```csharp
HandoffsWorkflowBuilder handoffBuilder =
    AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent);

handoffBuilder
    .WithHandoffs(triageAgent, [techSupportAgent, billingAgent])
    .WithHandoffs([techSupportAgent, billingAgent], escalationAgent);

Workflow workflow = handoffBuilder.Build();
```

### Group Chat

Multi-agent conversations with orchestrated turn-taking:

```csharp
GroupChatWorkflowBuilder groupChat =
    AgentWorkflowBuilder.CreateGroupChatBuilderWith(
        managerFactory: agents => new RoundRobinManager(agents));

groupChat
    .WithAgent(agent1)
    .WithAgent(agent2)
    .WithAgent(agent3);

Workflow workflow = groupChat.Build();
```

### GroupChatManager

**Location:** `dotnet/src/Microsoft.Agents.AI.Workflows/GroupChatManager.cs`

```csharp
public abstract class GroupChatManager
{
    // Select which agent speaks next
    protected internal abstract ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default);

    // Filter/transform history before passing to agent
    protected internal virtual ValueTask<IEnumerable<ChatMessage>> UpdateHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default);

    // Determine if conversation should end
    protected internal virtual ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default);
}
```

---

## 6. Human-in-the-Loop

### External Event Pattern

Using Durable Task for human intervention:

```csharp
[Function(nameof(RunOrchestrationAsync))]
public static async Task<object> RunOrchestrationAsync(
    [OrchestrationTrigger] TaskOrchestrationContext context)
{
    ContentGenerationInput input = context.GetInput<ContentGenerationInput>();
    DurableAIAgent writerAgent = context.GetAgent("WriterAgent");

    int iterationCount = 0;
    while (iterationCount++ < input.MaxReviewAttempts)
    {
        // Generate content
        AgentResponse<GeneratedContent> response =
            await writerAgent.RunAsync<GeneratedContent>(
                message: $"Write article about '{input.Topic}'",
                session: writerSession);

        // Notify human reviewer
        await context.CallActivityAsync(
            nameof(NotifyUserForApproval),
            response.Result);

        // Wait for human approval with timeout
        HumanApprovalResponse humanResponse =
            await context.WaitForExternalEvent<HumanApprovalResponse>(
                eventName: "HumanApproval",
                timeout: TimeSpan.FromHours(input.ApprovalTimeoutHours));

        if (humanResponse.Approved)
        {
            await context.CallActivityAsync(
                nameof(PublishContent),
                response.Result);
            return response.Result;
        }

        // Incorporate feedback and retry
        await writerAgent.RunAsync(
            message: $"Rewrite based on feedback: {humanResponse.Feedback}",
            session: writerSession);
    }

    return new { Status = "MaxRetriesExceeded" };
}
```

### Sending Human Feedback

```csharp
[Function(nameof(SendHumanApprovalAsync))]
public static async Task<HttpResponseData> SendHumanApprovalAsync(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post",
                 Route = "hitl/approve/{instanceId}")] HttpRequestData req,
    string instanceId,
    [DurableClient] DurableTaskClient client)
{
    HumanApprovalResponse approvalResponse =
        await req.ReadFromJsonAsync<HumanApprovalResponse>();

    // Raise event to waiting orchestration
    await client.RaiseEventAsync(
        instanceId,
        "HumanApproval",
        approvalResponse);

    return req.CreateResponse(HttpStatusCode.OK);
}
```

### Workflow Request Ports

**Location:** `dotnet/src/Microsoft.Agents.AI.Workflows/ExternalRequest.cs`

```csharp
public sealed class ExternalRequest
{
    public string PortId { get; }
    public PortableValue Data { get; }
}

public sealed class ExternalResponse
{
    public string RequestId { get; }
    public PortableValue? ResponseData { get; }
}
```

---

## 7. Checkpointing

### Checkpoint Architecture

**Location:** `dotnet/src/Microsoft.Agents.AI.Workflows/Checkpointing/`

```csharp
internal sealed class Checkpoint
{
    public int StepNumber { get; }
    public WorkflowInfo Workflow { get; }
    public RunnerStateData RunnerData { get; }
    public Dictionary<ScopeKey, PortableValue> StateData { get; }
    public Dictionary<EdgeId, PortableValue> EdgeStateData { get; }
    public CheckpointInfo? Parent { get; }

    public bool IsInitial => this.StepNumber == -1;
}
```

### ICheckpointManager

```csharp
internal interface ICheckpointManager
{
    // Save workflow state
    ValueTask<CheckpointInfo> CommitCheckpointAsync(
        string runId,
        Checkpoint checkpoint);

    // Restore workflow state
    ValueTask<Checkpoint> LookupCheckpointAsync(
        string runId,
        CheckpointInfo checkpointInfo);
}
```

### Implementations

| Implementation | Description | Use Case |
|----------------|-------------|----------|
| `InMemoryCheckpointManager` | In-memory storage | Development/testing |
| `JsonCheckpointStore` | File system-based | Local development |
| `CosmosCheckpointStore` | Azure Cosmos DB | Production |

### SuperStep Execution Model

- Workflows execute in discrete "SuperSteps"
- State changes are batched and applied between SuperSteps
- Enables consistent checkpointing and recovery
- Supports concurrent execution with proper isolation

---

## 8. DurableTask Integration

### DurableAIAgent

**Location:** `dotnet/src/Microsoft.Agents.AI.DurableTask/DurableAIAgent.cs`

Enables agents in Durable Task orchestrations:

```csharp
public sealed class DurableAIAgent : AIAgent
{
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Calls into durable entity
        return await this._context.Entities.CallEntityAsync<AgentResponse>(
            durableSession.SessionId,
            nameof(AgentEntity.Run),
            request);
    }
}
```

**Key Features:**
- Agents backed by durable entities
- Automatic state persistence
- Survives process restarts
- Supports long-running operations

### Usage in Orchestrations

```csharp
[Function(nameof(RunOrchestrationAsync))]
public static async Task<string> RunOrchestrationAsync(
    [OrchestrationTrigger] TaskOrchestrationContext context)
{
    // Get durable agent instance
    DurableAIAgent writer = context.GetAgent("WriterAgent");

    // Create session
    AgentSession session = await writer.GetNewSessionAsync();

    // Sequential agent calls with conversation continuity
    AgentResponse<TextResponse> initial =
        await writer.RunAsync<TextResponse>(
            message: "Write an inspirational sentence.",
            session: session);

    AgentResponse<TextResponse> refined =
        await writer.RunAsync<TextResponse>(
            message: $"Improve this: {initial.Result.Text}",
            session: session);

    return refined.Result.Text;
}
```

### Azure Functions Hosting

**Package:** `Microsoft.Agents.AI.Hosting.AzureFunctions`

```csharp
using IHost app = FunctionsApplication
    .CreateBuilder(args)
    .ConfigureFunctionsWebApplication()
    .ConfigureDurableAgents(options =>
    {
        options.AddAIAgent(myAgent);
        options.AddAIAgentFactory("DynamicAgent", sp =>
            CreateAgent(sp));
    })
    .Build();
```

---

## 9. Agent Builder Pattern

### AIAgentBuilder

**Location:** `dotnet/src/Microsoft.Agents.AI/AIAgentBuilder.cs`

Provides middleware/decorator pattern for agents:

```csharp
AIAgent agent = new AIAgentBuilder(innerAgent)
    .Use(async (messages, session, options, next, cancellationToken) =>
    {
        // Pre-processing
        Console.WriteLine($"Invoking agent with {messages.Count()} messages");

        // Delegate to next in pipeline
        await next(messages, session, options, cancellationToken);

        // Post-processing
        Console.WriteLine("Agent invocation complete");
    })
    .Build();
```

### Built-in Middleware

| Middleware | Purpose |
|------------|---------|
| `LoggingAgent` | Structured logging of agent interactions |
| `OpenTelemetryAgent` | Distributed tracing and metrics |
| `FunctionInvokingChatClient` | Automatic tool invocation |

---

## 10. Structured Output

### Response Format Support

Agents support structured output via `ChatResponseFormat`:

```csharp
// Define output schema
public sealed record TextResponse(string Text);

public sealed record GeneratedContent(
    string Title,
    string Body,
    List<string> Tags);

// Request structured response
AgentResponse<TextResponse> response =
    await agent.RunAsync<TextResponse>(
        message: "Generate content",
        session: session);

// Access typed result
string text = response.Result.Text;
```

### Continuation Tokens

Support for long-running/background operations:

```csharp
AgentRunOptions options = new()
{
    AllowBackgroundResponses = true
};

AgentResponse response = await agent.RunAsync(message, session, options);

// If operation is still running
if (response.ContinuationToken is not null)
{
    // Poll for completion
    AgentRunOptions pollOptions = new()
    {
        ContinuationToken = response.ContinuationToken
    };

    response = await agent.RunAsync(session: session, options: pollOptions);
}
```

---

## 11. Key File Paths

| Component | Path |
|-----------|------|
| AIAgent | `dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs` |
| AgentSession | `dotnet/src/Microsoft.Agents.AI.Abstractions/AgentSession.cs` |
| ChatClientAgent | `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs` |
| ChatClientAgentSession | `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgentSession.cs` |
| AIAgentBuilder | `dotnet/src/Microsoft.Agents.AI/AIAgentBuilder.cs` |
| Workflow | `dotnet/src/Microsoft.Agents.AI.Workflows/Workflow.cs` |
| WorkflowBuilder | `dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs` |
| AgentWorkflowBuilder | `dotnet/src/Microsoft.Agents.AI.Workflows/AgentWorkflowBuilder.cs` |
| GroupChatManager | `dotnet/src/Microsoft.Agents.AI.Workflows/GroupChatManager.cs` |
| Checkpointing | `dotnet/src/Microsoft.Agents.AI.Workflows/Checkpointing/` |
| DurableAIAgent | `dotnet/src/Microsoft.Agents.AI.DurableTask/DurableAIAgent.cs` |

---

## 12. Example: Complete Workflow

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

// Create chat client
var openAiClient = new OpenAIClient(apiKey);
var chatClient = openAiClient.GetChatClient("gpt-4");

// Define tools
var tools = new CustomerServiceTools(logger, orderService);

// Create agents
var triageAgent = chatClient.AsAIAgent(
    instructions: "Route customer inquiries to the appropriate specialist.",
    name: "Triage",
    tools: [AIFunctionFactory.Create(tools.ClassifyIntent)]);

var orderAgent = chatClient.AsAIAgent(
    instructions: "Handle order-related questions.",
    name: "Orders",
    tools: [
        AIFunctionFactory.Create(tools.LookupOrder),
        AIFunctionFactory.Create(tools.TrackShipment)
    ]);

var billingAgent = chatClient.AsAIAgent(
    instructions: "Handle billing and payment questions.",
    name: "Billing",
    tools: [
        AIFunctionFactory.Create(tools.GetInvoice),
        AIFunctionFactory.Create(tools.ProcessRefund)
    ]);

// Build handoff workflow
var workflow = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(triageAgent)
    .WithHandoffs(triageAgent, [orderAgent, billingAgent])
    .Build();

// Execute with streaming
await foreach (var update in workflow.RunStreamingAsync(
    "I need to track my order #12345"))
{
    Console.Write(update.Delta);
}
```

---

## 13. Integration Points

### Additional Packages

| Package | Purpose |
|---------|---------|
| `Microsoft.Agents.AI.Hosting.AzureFunctions` | Azure Functions hosting |
| `Microsoft.Agents.AI.Hosting.OpenAI` | OpenAI Realtime API support |
| `Microsoft.Agents.AI.CosmosNoSql` | Cosmos DB checkpointing |
| `Microsoft.Agents.AI.AGUI` | Agent GUI protocol |

### Samples

Key samples in `dotnet/samples/`:
- `AzureFunctions/05_AgentOrchestration_HITL/` - Human-in-the-loop
- `AzureFunctions/06_LongRunningTools/` - Long-running operations
- Multi-agent orchestration examples
