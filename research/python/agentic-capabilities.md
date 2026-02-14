# Python Agentic Capabilities - Deep Dive

**Date:** January 28, 2026

## Executive Summary

The Python implementation of Microsoft Agent Framework provides a comprehensive set of agentic capabilities through its modular package architecture. Key features include:

- **ChatAgent** - Primary agent implementation with tool support and streaming
- **Workflow System** - Pregel-inspired graph-based orchestration
- **Multi-Agent Patterns** - Group chat and handoff orchestration
- **Human-in-the-Loop** - Checkpointing and request info mechanisms
- **Middleware Pipeline** - Extensible request/response processing

---

## 1. Core Agent Abstractions

### ChatAgent

**Location:** `python/packages/core/agent_framework/_agents.py`

The `ChatAgent` is the primary agent implementation, decorated with middleware and instrumentation:

```python
@use_agent_middleware
@use_agent_instrumentation(capture_usage=False)
class ChatAgent(BaseAgent, Generic[TOptions_co]):
    def __init__(
        self,
        chat_client: ChatClientProtocol[TOptions_co],
        *,
        id: str | None = None,
        name: str | None = None,
        description: str | None = None,
        instructions: str | ChatMessage | Sequence[ChatMessage] | None = None,
        tools: ToolTypes | None = None,
        default_options: TOptions_co | None = None,
        context_provider: AIContextProvider | None = None,
        middleware: Sequence[AgentMiddleware | ChatMiddleware | FunctionMiddleware] | None = None,
        chat_message_store_factory: Callable[[], ChatMessageStore] | None = None,
        **kwargs: Any
    ):
```

**Key Parameters:**
- `chat_client`: Any `ChatClientProtocol` implementation (OpenAI, Anthropic, Azure, etc.)
- `instructions`: System-level guidance injected as system messages
- `tools`: Flexible tool registration (single tool, list, MCP tool, etc.)
- `default_options`: TypedDict for provider-specific chat options with IDE autocomplete
- `context_provider`: For RAG and dynamic context injection
- `middleware`: Pipeline for intercepting requests/responses
- `chat_message_store_factory`: Custom conversation persistence

### Core Methods

```python
# Non-streaming execution
async def run(
    self,
    messages: str | ChatMessage | Sequence[str | ChatMessage],
    *,
    thread: AgentThread | None = None,
    tools: ToolTypes | None = None,
    options: TOptions_co | None = None,
    **kwargs: Any
) -> AgentResponse:

# Streaming execution
def run_stream(
    self,
    messages: str | ChatMessage | Sequence[str | ChatMessage],
    *,
    thread: AgentThread | None = None,
    tools: ToolTypes | None = None,
    options: TOptions_co | None = None,
    **kwargs: Any
) -> AsyncIterable[AgentResponseUpdate]:
```

### Thread Management

```python
def get_new_thread(
    self,
    *,
    service_thread_id: str | None = None,
    **kwargs
) -> AgentThread:
```

Thread types:
- **Service-managed**: Chat history stored by the AI service (via `conversation_id`)
- **Local**: Chat history managed via `chat_message_store_factory`

### Agent-as-Tool

Convert any agent to a callable tool for composition:

```python
specialist_tool = specialist_agent.as_tool()
coordinator = ChatAgent(
    chat_client=client,
    tools=[specialist_tool]
)
```

### MCP Server Export

Expose agent capabilities via Model Context Protocol:

```python
mcp_server = agent.as_mcp_server()
```

---

## 2. Tool/Function Calling

### FunctionTool

**Location:** `python/packages/core/agent_framework/_tools.py`

```python
class FunctionTool(Generic[TInput, TOutput]):
    def __init__(
        self,
        func: Callable[..., TOutput | Awaitable[TOutput]],
        name: str | None = None,
        description: str | None = None,
        input_model: type[TInput] | None = None,
        approval_mode: ApprovalMode = "always_require",
        **kwargs
    )
```

### Tool Decorator

```python
from agent_framework import tool

@tool(
    name="get_weather",
    description="Get weather for a location",
    approval_mode="never_require"
)
async def get_weather(location: str, unit: str = "celsius") -> str:
    return f"Weather in {location}: 72°F"
```

### Approval Modes

| Mode | Behavior |
|------|----------|
| `"always_require"` | Human approval required before execution |
| `"never_require"` | Automatic execution |
| `"require_on_error"` | Approval only when execution fails |

### MCP Tool Integration

```python
from agent_framework import MCPTool

mcp_tool = MCPTool(server=mcp_server)
agent = ChatAgent(
    chat_client=client,
    tools=[mcp_tool]
)
```

### Function Invocation Configuration

```python
class FunctionInvocationConfiguration:
    max_retries: int = 3
    timeout: float = 60.0
    parallel_execution: bool = True
```

---

## 3. ChatClient Abstractions

### ChatClientProtocol

**Location:** `python/packages/core/agent_framework/_clients.py`

```python
@runtime_checkable
class ChatClientProtocol(Protocol[TOptions_contra]):
    additional_properties: dict[str, Any]

    async def get_response(
        self,
        messages: str | ChatMessage | Sequence[str | ChatMessage],
        *,
        options: TOptions_contra | None = None,
        **kwargs: Any
    ) -> ChatResponse

    def get_streaming_response(
        self,
        messages: str | ChatMessage | Sequence[str | ChatMessage],
        *,
        options: TOptions_contra | None = None,
        **kwargs: Any
    ) -> AsyncIterable[ChatResponseUpdate]
```

### Lazy Loading Pattern

Providers are lazily loaded from connector packages:

```python
# User imports from consistent location
from agent_framework.openai import OpenAIChatClient
from agent_framework.azure import AzureOpenAIChatClient
from agent_framework.anthropic import AnthropicChatClient

# Internally uses __getattr__ for lazy loading
# If package not installed, provides helpful error message
```

### Client-to-Agent Convenience

```python
agent = client.as_agent(
    instructions="You are helpful",
    tools=[tool1, tool2]
)
```

---

## 4. Workflow System

### Workflow Class

**Location:** `python/packages/core/agent_framework/_workflows/_workflow.py`

```python
class Workflow(DictConvertible):
    def __init__(
        self,
        edge_groups: list[EdgeGroup],
        executors: dict[str, Executor],
        start_executor: Executor,
        runner_context: RunnerContext,
        max_iterations: int = DEFAULT_MAX_ITERATIONS,
        name: str | None = None,
        description: str | None = None
    )
```

### Execution Methods

```python
# Non-streaming with optional checkpointing
async def run(
    self,
    message: Any | None = None,
    *,
    checkpoint_id: str | None = None,
    checkpoint_storage: CheckpointStorage | None = None,
    include_status_events: bool = False,
    **kwargs: Any
) -> WorkflowRunResult

# Streaming with events
async def run_stream(
    self,
    message: Any | None = None,
    *,
    checkpoint_id: str | None = None,
    checkpoint_storage: CheckpointStorage | None = None,
    include_status_events: bool = False,
    **kwargs: Any
) -> AsyncIterable[WorkflowEvent]

# Send HITL responses
async def send_responses(
    responses: dict[str, Any]
) -> WorkflowRunResult

async def send_responses_streaming(
    responses: dict[str, Any]
) -> AsyncIterable[WorkflowEvent]
```

### Executor

**Location:** `python/packages/core/agent_framework/_workflows/_executor.py`

Base class for workflow nodes:

```python
class Executor(RequestInfoMixin, DictConvertible):
    @handler
    async def handle_message(
        self,
        message: T,
        ctx: WorkflowContext[T_Out, T_W_Out]
    ) -> None:
        await ctx.send_message(result)      # Send to other executors
        await ctx.yield_output(output)      # Yield workflow output
        await ctx.request_info(req, type)   # Request human input
```

### WorkflowContext Types

```python
WorkflowContext                    # Side effects only
WorkflowContext[T_Out]             # Send messages of type T_Out
WorkflowContext[T_Out, T_W_Out]    # Send messages AND yield workflow outputs
```

### Edge Types

```python
from agent_framework._workflows import (
    SingleEdge,      # One-to-one connection
    FanOutEdge,      # One-to-many broadcast
    FanInEdge,       # Many-to-one aggregation
    SwitchCaseEdge   # Conditional routing
)
```

---

## 5. Multi-Agent Patterns

### Group Chat

**Location:** `python/packages/core/agent_framework/_workflows/_group_chat.py`

```python
from agent_framework import GroupChatBuilder

workflow = (
    GroupChatBuilder()
    .with_orchestrator(
        agent=orchestrator_agent,           # Agent-based selection
        # OR
        selection_func=round_robin_selector # Function-based selection
    )
    .participants([agent1, agent2, agent3])
    .with_termination_condition(condition)
    .with_max_rounds(10)
    .with_checkpointing(checkpoint_storage)
    .with_request_info(agents=["agent1"])   # Enable HITL for specific agents
    .build()
)
```

### Selection Function

```python
async def round_robin_selector(state: GroupChatState) -> str:
    """
    state.current_round: int
    state.participants: OrderedDict[str, str]
    state.conversation: list[ChatMessage]
    """
    participants = list(state.participants.keys())
    return participants[state.current_round % len(participants)]
```

### Agent-Based Orchestrator

Uses structured output to select next speaker:

```python
class AgentBasedGroupChatOrchestrator(BaseGroupChatOrchestrator):
    async def _invoke_agent(self) -> AgentOrchestrationOutput:
        response = await self._agent.run(
            messages=conversation,
            options={"response_format": AgentOrchestrationOutput}
        )
        return AgentOrchestrationOutput.model_validate_json(response.text)
```

### Handoff Pattern

**Location:** `python/packages/core/agent_framework/_workflows/_handoff.py`

```python
from agent_framework import HandoffBuilder

workflow = (
    HandoffBuilder(
        name="support-workflow",
        participants=[triage, billing, support]
    )
    .with_start_agent(triage)
    .add_handoff(triage, [billing, support])
    .add_handoff([billing, support], escalation)
    .with_autonomous_mode(
        agents=["billing", "support"],
        prompts={"billing": "Continue assisting with billing"},
        turn_limits={"billing": 5}
    )
    .with_termination_condition(condition)
    .with_checkpointing(checkpoint_storage)
    .build()
)
```

### Handoff Tool Generation

Handoff executors automatically create handoff tools:

```python
class HandoffAgentExecutor(AgentExecutor):
    def _create_handoff_tool(self, target_id: str, description: str | None) -> FunctionTool:
        @tool(
            name=f"handoff_to_{target_id}",
            description=description,
            approval_mode="never_require"
        )
        def _handoff_tool(context: str | None = None) -> str:
            return f"Handoff to {target_id}"
        return _handoff_tool
```

---

## 6. Human-in-the-Loop

### Checkpointing

**Location:** `python/packages/core/agent_framework/_workflows/_checkpoint.py`

#### WorkflowCheckpoint

```python
@dataclass(slots=True)
class WorkflowCheckpoint:
    checkpoint_id: str
    workflow_id: str
    timestamp: str

    # Core workflow state
    messages: dict[str, list[dict[str, Any]]]
    shared_state: dict[str, Any]
    pending_request_info_events: dict[str, dict[str, Any]]

    # Runtime state
    iteration_count: int

    # Metadata
    metadata: dict[str, Any]
    version: str = "1.0"
```

#### CheckpointStorage Protocol

```python
class CheckpointStorage(Protocol):
    async def save_checkpoint(self, checkpoint: WorkflowCheckpoint) -> str
    async def load_checkpoint(self, checkpoint_id: str) -> WorkflowCheckpoint | None
    async def list_checkpoint_ids(self, workflow_id: str | None = None) -> list[str]
    async def list_checkpoints(self, workflow_id: str | None = None) -> list[WorkflowCheckpoint]
    async def delete_checkpoint(self, checkpoint_id: str) -> bool
```

#### Implementations

```python
# In-memory (development/testing)
from agent_framework import InMemoryCheckpointStorage
storage = InMemoryCheckpointStorage()

# File-based (JSON persistence)
from agent_framework import FileCheckpointStorage
storage = FileCheckpointStorage(path="./checkpoints")
```

#### Usage

```python
# Enable at build time
workflow = builder.with_checkpointing(storage).build()

# Enable at runtime
result = await workflow.run(message, checkpoint_storage=runtime_storage)

# Resume from checkpoint
result = await workflow.run(
    checkpoint_id="cp_123",
    checkpoint_storage=storage
)
```

### Request Info Pattern

**Location:** `python/packages/core/agent_framework/_workflows/_request_info_mixin.py`

#### Requesting Human Input

```python
class ApprovalExecutor(Executor):
    @handler
    async def process(self, message: str, ctx: WorkflowContext[str]) -> None:
        await ctx.request_info(
            ApprovalRequest(message="Proceed with action?"),
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
            await ctx.send_message("Action approved")
        else:
            await ctx.send_message("Action rejected")
```

#### Workflow Execution with HITL

```python
# Initial run
result = await workflow.run("start")

# Check for pending requests
requests = result.get_request_info_events()

# Gather human responses
responses = {}
for req in requests:
    responses[req.request_id] = get_human_approval(req.data)

# Continue with responses
result = await workflow.send_responses(responses)
```

---

## 7. Event System

### Workflow Events

```python
# Lifecycle
WorkflowStartedEvent
WorkflowStatusEvent(state: WorkflowRunState)
WorkflowOutputEvent(data: Any)
WorkflowFailedEvent(details: WorkflowErrorDetails)

# Executor
ExecutorInvokedEvent(executor_id: str, input_data: Any)
ExecutorCompletedEvent(executor_id: str, output_data: Any)
ExecutorFailedEvent(executor_id: str, error: WorkflowErrorDetails)

# Agent
AgentRunEvent(executor_id: str, data: AgentResponse)
AgentRunUpdateEvent(executor_id: str, data: AgentResponseUpdate)

# Human-in-the-Loop
RequestInfoEvent(request_id: str, data: Any, response_type: type)
HandoffSentEvent(source: str, target: str)
```

### Streaming with Events

```python
async for event in workflow.run_stream(message, include_status_events=True):
    match event:
        case WorkflowStartedEvent():
            print("Workflow started")
        case AgentRunUpdateEvent(executor_id=eid, data=update):
            print(f"[{eid}] {update.delta}")
        case WorkflowOutputEvent(data=output):
            print(f"Output: {output}")
        case RequestInfoEvent(request_id=rid, data=data):
            # Handle HITL request
            pass
```

---

## 8. Middleware Architecture

### FunctionMiddleware

```python
class FunctionMiddleware:
    async def process(
        self,
        context: FunctionInvocationContext,
        next: Callable[[FunctionInvocationContext], Awaitable[None]]
    ) -> None:
        # Pre-processing
        print(f"Calling: {context.function_name}")

        await next(context)  # Call next middleware or function

        # Post-processing
        print(f"Result: {context.result}")
```

### ChatMiddleware

```python
class ChatMiddleware:
    async def process(
        self,
        context: ChatContext,
        next: Callable[[ChatContext], Awaitable[None]]
    ) -> None:
        # Modify messages before
        context.messages = [msg.with_metadata(...) for msg in context.messages]

        await next(context)

        # Process response after
        log_response(context.response)
```

### AgentMiddleware

```python
class AgentMiddleware:
    async def process(
        self,
        context: AgentContext,
        next: Callable[[AgentContext], Awaitable[None]]
    ) -> None:
        await next(context)
```

### Built-in Middleware

- **Function approval middleware** - Handles approval_mode logic
- **Retry middleware** - Handles retries and timeouts
- **Telemetry middleware** - OpenTelemetry instrumentation
- **MCP interception** - Tool interception in handoff patterns

---

## 9. Type System

### Executor Type Annotations

```python
# Input types: What an executor can receive
executor.input_types -> list[type[Any]]

# Output types: What an executor can send via ctx.send_message()
executor.output_types -> list[type[Any]]

# Workflow output types: What an executor can yield via ctx.yield_output()
executor.workflow_output_types -> list[type[Any]]
```

### Build-time Validation

The workflow builder validates type compatibility between connected executors at build time.

---

## 10. Key File Paths

| Component | Path |
|-----------|------|
| ChatAgent | `python/packages/core/agent_framework/_agents.py` |
| FunctionTool | `python/packages/core/agent_framework/_tools.py` |
| ChatClient Protocol | `python/packages/core/agent_framework/_clients.py` |
| Workflow | `python/packages/core/agent_framework/_workflows/_workflow.py` |
| Executor | `python/packages/core/agent_framework/_workflows/_executor.py` |
| WorkflowContext | `python/packages/core/agent_framework/_workflows/_workflow_context.py` |
| Group Chat | `python/packages/core/agent_framework/_workflows/_group_chat.py` |
| Handoff | `python/packages/core/agent_framework/_workflows/_handoff.py` |
| Checkpointing | `python/packages/core/agent_framework/_workflows/_checkpoint.py` |
| Request Info | `python/packages/core/agent_framework/_workflows/_request_info_mixin.py` |

---

## Example: Complete Workflow

```python
from agent_framework import (
    ChatAgent, tool, HandoffBuilder,
    FileCheckpointStorage, TerminationCondition
)
from agent_framework.openai import OpenAIChatClient

# Create client
client = OpenAIChatClient(api_key=os.environ["OPENAI_API_KEY"])

# Define tools
@tool(name="lookup_order", approval_mode="never_require")
async def lookup_order(order_id: str) -> str:
    return f"Order {order_id}: Shipped, arriving tomorrow"

# Create agents
triage = ChatAgent(
    chat_client=client,
    instructions="You are a triage agent. Route to billing or support.",
    name="triage"
)

support = ChatAgent(
    chat_client=client,
    instructions="You handle support questions.",
    name="support",
    tools=[lookup_order]
)

billing = ChatAgent(
    chat_client=client,
    instructions="You handle billing questions.",
    name="billing"
)

# Build workflow with checkpointing
storage = FileCheckpointStorage(path="./checkpoints")

workflow = (
    HandoffBuilder(participants=[triage, billing, support])
    .with_start_agent(triage)
    .add_handoff(triage, [billing, support])
    .with_autonomous_mode(agents=["billing", "support"])
    .with_checkpointing(storage)
    .build()
)

# Execute
async for event in workflow.run_stream("I need help with my order #12345"):
    if isinstance(event, AgentRunUpdateEvent):
        print(event.data.delta, end="", flush=True)
```
