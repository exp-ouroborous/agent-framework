# Agentic Capabilities in Microsoft Agent Framework

**Date:** January 28, 2026
**Scope:** Cross-language overview (Python & .NET)

## Executive Summary

Microsoft Agent Framework provides comprehensive agentic capabilities across both Python and .NET implementations. The framework supports:

1. **Core Agent Abstractions** - ChatAgent/AIAgent with tool calling, streaming, and session management
2. **Multi-Agent Orchestration** - Graph-based workflows, group chats, and handoff patterns
3. **Human-in-the-Loop (HITL)** - Checkpointing, approval flows, and intervention mechanisms
4. **Observability** - OpenTelemetry integration for tracing and metrics

Both implementations share architectural principles while adapting to language idioms.

---

## Architecture Overview

### Key Abstractions

| Concept | Python | .NET |
|---------|--------|------|
| Base Agent | `ChatAgent` | `AIAgent` / `ChatClientAgent` |
| Chat Client | `ChatClientProtocol` | `IChatClient` (M.E.AI) |
| Tools | `FunctionTool`, `@tool` decorator | `AITool`, `AIFunctionFactory` |
| Sessions | `AgentThread` | `AgentSession` |
| Workflows | `Workflow`, `Executor` | `Workflow`, `Executor` |
| Checkpoints | `WorkflowCheckpoint` | `Checkpoint` |

### Package Structure

**Python (Monorepo):**
```
python/packages/
├── core/agent_framework/     # Core abstractions
├── azure-ai/                 # Azure AI provider
├── openai/                   # OpenAI provider
├── anthropic/                # Anthropic provider
└── ...                       # 22+ packages
```

**\.NET:**
```
dotnet/src/
├── Microsoft.Agents.AI.Abstractions/   # Core interfaces
├── Microsoft.Agents.AI/                # Implementations
├── Microsoft.Agents.AI.Workflows/      # Orchestration
├── Microsoft.Agents.AI.DurableTask/    # Durable execution
└── Microsoft.Agents.AI.Hosting.*/      # Hosting packages
```

---

## Core Agent Capabilities

### 1. Agent Invocation

Both platforms support streaming and non-streaming agent execution:

**Python:**
```python
from agent_framework import ChatAgent, tool

agent = ChatAgent(
    chat_client=client,
    instructions="You are a helpful assistant",
    tools=[my_tool]
)

# Non-streaming
response = await agent.run(messages, thread=thread)

# Streaming
async for update in agent.run_stream(messages, thread=thread):
    print(update.delta)
```

**\.NET:**
```csharp
var agent = new ChatClientAgent(
    chatClient,
    instructions: "You are a helpful assistant",
    tools: [myTool]);

// Non-streaming
AgentResponse response = await agent.RunAsync(messages, session);

// Streaming
await foreach (var update in agent.RunStreamingAsync(messages, session))
{
    Console.Write(update.Delta);
}
```

### 2. Tool/Function Calling

Tools are first-class citizens with automatic schema generation:

**Python:**
```python
from agent_framework import tool

@tool(
    name="get_weather",
    description="Get current weather for a location",
    approval_mode="never_require"
)
async def get_weather(location: str, unit: str = "celsius") -> str:
    return f"Weather in {location}: 72°F"
```

**\.NET:**
```csharp
[Description("Get current weather for a location")]
public string GetWeather(
    [Description("The location to get weather for")] string location,
    [Description("Temperature unit")] string unit = "celsius")
{
    return $"Weather in {location}: 72°F";
}

// Register with AIFunctionFactory
var tool = AIFunctionFactory.Create(GetWeather);
```

### 3. Agent-as-Tool Pattern

Agents can be composed by converting them to tools:

**Python:**
```python
specialist_tool = specialist_agent.as_tool()
coordinator = ChatAgent(
    chat_client=client,
    tools=[specialist_tool]
)
```

---

## Multi-Agent Orchestration

### Workflow System

Both implementations use a graph-based Pregel-inspired model with "SuperStep" execution:

**Python:**
```python
from agent_framework import WorkflowBuilder, AgentExecutor

builder = WorkflowBuilder(start_executor)
builder.add_edge(agent1, agent2)
builder.add_conditional_edge(agent2, router_func, {"yes": agent3, "no": agent4})
workflow = builder.build()

result = await workflow.run(message)
```

**\.NET:**
```csharp
var builder = new WorkflowBuilder(startExecutor);
builder.AddEdge(agent1, agent2);
builder.AddEdge<T>(agent2, agent3, condition: x => x.Score > 0.5);
var workflow = builder.Build();
```

### Pre-Built Patterns

#### Group Chat

Multi-agent conversations with orchestrated turn-taking:

**Python:**
```python
workflow = (
    GroupChatBuilder()
    .with_orchestrator(selection_func=round_robin_selector)
    .participants([agent1, agent2, agent3])
    .with_max_rounds(10)
    .with_termination_condition(condition)
    .build()
)
```

**\.NET:**
```csharp
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => new RoundRobinManager(agents))
    .WithAgent(agent1)
    .WithAgent(agent2)
    .Build();
```

#### Handoff Pattern

Agents delegate control to specialists:

**Python:**
```python
workflow = (
    HandoffBuilder(participants=[triage, billing, support])
    .with_start_agent(triage)
    .add_handoff(triage, [billing, support])
    .add_handoff([billing, support], escalation)
    .with_autonomous_mode(
        agents=["billing", "support"],
        turn_limits={"billing": 5}
    )
    .build()
)
```

**\.NET:**
```csharp
var workflow = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(triageAgent)
    .WithHandoffs(triageAgent, [techSupportAgent, billingAgent])
    .WithHandoffs([techSupportAgent, billingAgent], escalationAgent)
    .Build();
```

#### Sequential Pipeline

Chain agents where output feeds into the next:

**\.NET:**
```csharp
var workflow = AgentWorkflowBuilder.BuildSequential(
    workflowName: "ContentPipeline",
    agents: [researcherAgent, writerAgent, editorAgent]);
```

---

## Human-in-the-Loop (HITL)

### Checkpointing

Save and restore workflow state for durability and intervention:

**Python:**
```python
from agent_framework import FileCheckpointStorage

storage = FileCheckpointStorage(path="./checkpoints")

# Enable checkpointing
workflow = builder.with_checkpointing(storage).build()

# Resume from checkpoint
result = await workflow.run(checkpoint_id="cp_123", checkpoint_storage=storage)
```

**Checkpoint Data:**
- Executor message queues
- Shared workflow state
- Pending request info events
- Iteration counts and metadata

### Request Info Pattern (Python)

Request external input during workflow execution:

```python
class ApprovalExecutor(Executor):
    @handler
    async def process(self, message: str, ctx: WorkflowContext[str]) -> None:
        # Request human approval
        await ctx.request_info(
            ApprovalRequest(message="Proceed?"),
            bool  # response_type
        )

    @response_handler
    async def handle_approval(
        self,
        original_request: ApprovalRequest,
        approved: bool,
        ctx: WorkflowContext[str]
    ) -> None:
        if approved:
            await ctx.send_message("Approved!")
```

**Workflow execution with HITL:**
```python
result = await workflow.run("start")
requests = result.get_request_info_events()

# Gather human responses
responses = {req.request_id: True for req in requests}
result = await workflow.send_responses(responses)
```

### External Events (.NET with Durable Task)

```csharp
[Function(nameof(RunOrchestrationAsync))]
public static async Task<object> RunOrchestrationAsync(
    [OrchestrationTrigger] TaskOrchestrationContext context)
{
    // Generate content
    var response = await writerAgent.RunAsync<GeneratedContent>(message, session);

    // Wait for human approval
    HumanApprovalResponse approval = await context.WaitForExternalEvent<HumanApprovalResponse>(
        eventName: "HumanApproval",
        timeout: TimeSpan.FromHours(24));

    if (approval.Approved)
        await context.CallActivityAsync(nameof(PublishContent), response.Result);
}
```

### Tool Approval

Control which tools require human approval:

**Python:**
```python
@tool(approval_mode="always_require")  # Human approval needed
async def delete_file(path: str) -> str: ...

@tool(approval_mode="never_require")   # Auto-execute
async def read_file(path: str) -> str: ...

@tool(approval_mode="require_on_error") # Approve only on failure
async def api_call(endpoint: str) -> str: ...
```

---

## Key Architectural Decisions (ADRs)

| ADR | Title | Status | Key Decision |
|-----|-------|--------|--------------|
| 0001 | Agent Run Response | Accepted | New `AgentResponse`/`AgentResponseUpdate` types |
| 0002 | Agent Tools | Proposed | Generic tool abstractions with provider-specific extensions |
| 0006 | User Approval | Accepted | `UserInputRequestContent`/`UserInputResponseContent` types |
| 0007 | Agent Filtering | Proposed | Decorator pattern for middleware |
| 0008 | Python Subpackages | Accepted | Vendor-based folders with lazy loading |
| 0009 | Long-Running Ops | Accepted | Polling and continuation token patterns |
| 0014 | Feature Collections | Accepted | AdditionalProperties dictionary for extensibility |

---

## Event-Driven Architecture

### Workflow Events (Python)

```python
# Workflow lifecycle
WorkflowStartedEvent
WorkflowStatusEvent(state: WorkflowRunState)
WorkflowOutputEvent(data: Any)
WorkflowFailedEvent(details: WorkflowErrorDetails)

# Executor events
ExecutorInvokedEvent(executor_id: str, input_data: Any)
ExecutorCompletedEvent(executor_id: str, output_data: Any)

# Agent events
AgentRunEvent(executor_id: str, data: AgentResponse)
AgentRunUpdateEvent(executor_id: str, data: AgentResponseUpdate)

# HITL events
RequestInfoEvent(request_id: str, data: Any, response_type: type)
HandoffSentEvent(source: str, target: str)
```

---

## Middleware Architecture

### Python

```python
class FunctionMiddleware:
    async def process(
        self,
        context: FunctionInvocationContext,
        next: Callable[[FunctionInvocationContext], Awaitable[None]]
    ) -> None:
        # Pre-processing
        await next(context)
        # Post-processing
```

### \.NET

```csharp
AIAgent agent = new AIAgentBuilder(innerAgent)
    .Use(async (messages, session, options, next, ct) => {
        // Pre-processing
        await next(messages, session, options, ct);
        // Post-processing
    })
    .Build();
```

---

## Session/Thread Management

### Python

```python
# Create thread
thread = agent.get_new_thread()

# Service-managed thread
thread = agent.get_new_thread(service_thread_id="conv_123")

# Persist locally
agent = ChatAgent(
    chat_client=client,
    chat_message_store_factory=lambda: InMemoryMessageStore()
)
```

### \.NET

```csharp
// Create session
AgentSession session = await agent.GetNewSessionAsync();

// Service-backed (conversation ID)
session.ConversationId = "conv_123";

// Client-backed
session.ChatHistoryProvider = new InMemoryChatHistoryProvider();
```

---

## Key File Paths

### Python
- Core agents: `python/packages/core/agent_framework/_agents.py`
- Tools: `python/packages/core/agent_framework/_tools.py`
- Workflows: `python/packages/core/agent_framework/_workflows/`
- Group chat: `python/packages/core/agent_framework/_workflows/_group_chat.py`
- Handoff: `python/packages/core/agent_framework/_workflows/_handoff.py`
- Checkpoints: `python/packages/core/agent_framework/_workflows/_checkpoint.py`

### \.NET
- Agent abstractions: `dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs`
- ChatClientAgent: `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs`
- Workflows: `dotnet/src/Microsoft.Agents.AI.Workflows/`
- Durable agents: `dotnet/src/Microsoft.Agents.AI.DurableTask/`

---

## Recommendations for Learning

1. **Start with samples:**
   - Python: `python/samples/`
   - .NET: `dotnet/samples/`

2. **Understand core abstractions first:**
   - `ChatAgent` (Python) / `AIAgent` (.NET)
   - Tool registration and invocation
   - Session/thread management

3. **Progress to multi-agent patterns:**
   - Simple sequential workflows
   - Group chat orchestration
   - Handoff patterns

4. **Explore HITL when needed:**
   - Checkpointing for durability
   - Approval flows for oversight
   - External events for integration

5. **Review ADRs for design rationale:**
   - Especially ADR-0002 (tools) and ADR-0008 (Python structure)
