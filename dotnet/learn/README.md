# AgentLearn

A .NET 10 web API for learning Microsoft Agent Framework features: typed workflows, human-in-the-loop (HITL), fan-out/fan-in concurrency, tool use, and OpenTelemetry tracing. Supports multiple LLM providers (GitHub Models, OpenAI, Azure OpenAI, Anthropic) via a pluggable `ChatClientFactory`.

## Prerequisites

- .NET 10.0 SDK
- An API key for at least one supported LLM provider (or use the `Mock` provider)
- Docker (optional, for Jaeger tracing)

## Configuration

Configure the LLM provider in `appsettings.json` (or `appsettings.Development.json`):

```json
{
  "Llm": {
    "Provider": "OpenAI",
    "Model": "gpt-4o-mini"
  }
}
```

Supported providers and their environment variables:

| Provider | `Llm:Provider` | API Key Env Var | Additional Env Vars |
|---|---|---|---|
| OpenAI | `OpenAI` | `OPENAI_API_KEY` | |
| GitHub Models | `GitHub` | `GH_PAT` | |
| Azure OpenAI | `AzureOpenAI` | `AZURE_OPENAI_API_KEY` | `AZURE_OPENAI_ENDPOINT` |
| Anthropic | `Anthropic` | `ANTHROPIC_API_KEY` | |
| Mock | `Mock` | (none) | |

API keys can also be set via `Llm:ApiKey` in configuration instead of environment variables.

## Running

```bash
cd AgentLearn
dotnet run
```

Or use the convenience script that sets up Jaeger and runs the server:

```bash
cd AgentLearn
./run.sh
```

The server starts at `https://localhost:7219` (or `http://localhost:5282`).

## Testing

Integration tests use MSTest + Moq and exercise full workflows with a mock `IChatClient`:

```bash
dotnet test AgentLearn/tests/integration --verbosity normal
```

## Task Kinds

### JokeWriter

Generates jokes using three workflow modes:

| Mode | Pipeline | Description |
|------|----------|-------------|
| `Single` | Writer | One agent writes a joke |
| `Sequential` | Writer → Critic → Editor | Three-stage refinement |
| `Concurrent` | 3x (Writer → Critic → Editor) → Aggregator → Selector | Parallel pipelines with best-of selection |

```bash
curl -X POST http://localhost:5282/task \
  -H "Content-Type: application/json" \
  -d '{"task": "Tell me a joke about programming", "mode": "Sequential", "kind": "JokeWriter"}'
```

### StoryGenerator

Generates short stories with human-in-the-loop (HITL). The workflow pauses via a `RequestPort` to ask for a character name, then the Storyteller agent (with a `GetLuckyNumber` tool) writes a 2-sentence story.

Only supports `Single` mode.

```bash
curl -X POST http://localhost:5282/task \
  -H "Content-Type: application/json" \
  -d '{"task": "Alice", "mode": "Single", "kind": "StoryGenerator"}'
```

## Architecture

### Request Flow

```
HTTP POST /task
  → TaskController
    → ITaskHandler (routed by TaskKind)
      → JokeWriterHandler / StoryGeneratorHandler
        → Selects Workflow by AgentMode
        → InProcessExecution.StreamAsync(workflow, input)
        → Streams WorkflowEvents, extracts typed output
  → TaskResponse
```

### Typed Workflow Boundaries

Workflows use typed I/O rather than raw chat messages. Custom executors at the boundaries handle the conversion:

**JokeWriter executors:**
- **JokeInputExecutor** — Dual-protocol entry point: accepts `JokeRequest` (typed API) or `List<ChatMessage>` (DevUI chat)
- **JokeOutputExecutor** — Extracts the final assistant message, emits both `JokeOutput` and `List<ChatMessage>`
- **JokeAggregatorExecutor** — Thread-safe fan-in collector for concurrent mode

**StoryGenerator executors:**
- **StoryNameBridgeExecutor** — Converts the HITL string response into `ChatMessage` + `TurnToken`
- **StoryOutputExecutor** — Extracts the final assistant message, emits both `StoryOutput` and `List<ChatMessage>`

### DevUI & OpenAI-Compatible Endpoints

All workflows are registered with `.AddAsAIAgent()` for DevUI discovery. Browse to `/devui` in development mode to chat with any agent or workflow interactively.

OpenAI-compatible `/responses` and `/conversations` endpoints are also mapped, so external clients (curl, SDKs, ChatGPT-compatible UIs) can talk to any registered agent.

## Project Structure

```
AgentLearn/
├── Controllers/
│   └── TaskController.cs               # POST /task endpoint
├── Extensions/
│   └── AIAgentBuilderExtensions.cs     # Tool invocation logging middleware
├── Models/
│   ├── AgentMode.cs                    # Single, Sequential, Concurrent
│   ├── TaskKind.cs                     # JokeWriter, StoryGenerator
│   ├── TaskRequest.cs / TaskResponse.cs
│   └── LlmOptions.cs                  # LLM provider configuration
├── Services/
│   ├── ChatClientFactory.cs            # Multi-provider IChatClient creation + MockChatClient
│   └── ITaskHandler.cs                 # Task handler interface
├── TaskKinds/
│   ├── JokeWriter/
│   │   ├── JokeWriter.cs              # Models, executors, agents, handler, DI registration
│   │   └── JokeWriter.http            # REST Client test requests
│   └── StoryGenerator/
│       ├── StoryGenerator.cs           # Models, executors, agents, handler, DI registration
│       └── StoryGenerator.http         # REST Client test requests
├── tests/
│   └── integration/
│       ├── AgentLearn.IntegrationTests.csproj
│       ├── MockChatClientHelper.cs     # Moq-based IChatClient helper
│       ├── JokeWriterWorkflowTests.cs  # 3 tests: Single, Sequential, Concurrent
│       └── StoryGeneratorWorkflowTests.cs  # 2 tests: HITL workflow, mode rejection
├── Program.cs                          # DI, OpenTelemetry, DevUI, OpenAI endpoints
├── CLAUDE.md                           # Project-specific code conventions
└── run.sh                              # Starts Jaeger + runs server
```

Each TaskKind is self-contained in a single file: models, executors, agent factories, workflow builders, handler, and DI registration.

## Packages

- `Microsoft.Agents.AI` — Core agent abstractions
- `Microsoft.Agents.AI.OpenAI` — OpenAI/GitHub Models integration
- `Microsoft.Agents.AI.Workflows` — Workflow orchestration
- `Microsoft.Agents.AI.DevUI` — Development UI for interactive chat testing
- `Microsoft.Agents.AI.Hosting` / `Microsoft.Agents.AI.Hosting.OpenAI` — Agent hosting
- `OpenAI` / `Azure.AI.OpenAI` — LLM provider SDKs
- `Anthropic` — Anthropic provider SDK
- `OpenTelemetry.*` — Distributed tracing

## OpenTelemetry with Jaeger

The project exports traces to Jaeger via OTLP.

### Start Jaeger

```bash
docker run -d --name jaeger \
  -e COLLECTOR_OTLP_ENABLED=true \
  -p 16686:16686 \
  -p 4317:4317 \
  -p 4318:4318 \
  jaegertracing/all-in-one:latest
```

### View Traces

Open Jaeger UI at http://localhost:16686 and select service "AgentLearn" to view traces of agent executions, including:
- HTTP request handling
- AI chat completions
- Tool invocations
- Workflow orchestration

## MSAF .NET Feature Coverage

What this project demonstrates vs. what the framework offers. Features are grouped by area; "Used in" shows where AgentLearn exercises the feature (or "—" if not yet explored).

### Agents

| Feature | Used in | Notes |
|---------|---------|-------|
| `ChatClientAgent` | JokeWriter, StoryGenerator | Primary agent type throughout |
| `DelegatingAIAgent` | — | Wrapper/composition pattern |
| `AIAgentBuilder` / `.AsBuilder()` | JokeWriter, StoryGenerator | Fluent agent configuration |
| Agent middleware (run-level) | — | Intercept `RunAsync` for guardrails, PII filtering |
| Function invocation middleware | `UseToolInvocationLogging` | Logs tool calls; framework also supports pre/post interception |
| Structured output (`RunAsync<T>`) | — | Typed JSON schema responses |
| Agent handoffs | — | Transfer control between agents |
| Agent sessions / thread management | — | Multi-turn conversation state |

### Tools & Function Calling

| Feature | Used in | Notes |
|---------|---------|-------|
| `AIFunctionFactory.Create` | JokeWriter (`GetDayOfYear`), StoryGenerator (`GetLuckyNumber`) | Basic tool creation from delegates |
| `ApprovalRequiredAIFunction` | — | HITL tool approval; agent pauses before executing |
| OpenAPI tool generation | — | Create tools from OpenAPI specs |
| MCP client tools | — | Model Context Protocol integration |
| MCP server (expose agents as tools) | — | Serve agent capabilities over MCP |

### Workflows

| Feature | Used in | Notes |
|---------|---------|-------|
| `WorkflowBuilder` | JokeWriter, StoryGenerator | Manual graph construction |
| `AgentWorkflowBuilder.BuildSequential` | JokeWriter (pipelines) | Helper for linear chains |
| Sequential workflow | JokeWriter (`JokeSequentialWorkflow`) | Writer → Critic → Editor |
| Concurrent fan-out / fan-in | JokeWriter (`JokeConcurrentWorkflow`) | 3 parallel pipelines → aggregator → selector |
| Sub-workflows | JokeWriter (pipelines bound via `BindAsExecutor`) | Nested workflow composition |
| Custom executors | JokeInputExecutor, JokeOutputExecutor, JokeAggregatorExecutor, StoryNameBridgeExecutor, StoryOutputExecutor | Typed I/O boundaries |
| `RequestPort` (workflow HITL) | StoryGenerator (`AskCharacterName`) | Workflow pauses for external input |
| Loop / iterative workflows | — | Repeat with exit conditions |
| Conditional / branching edges | — | Dynamic routing based on output |
| Checkpointing | — | Save/restore workflow state |
| Time travel (replay) | — | Replay to previous checkpoint |
| Declarative workflows (YAML) | — | Define workflows in YAML |

### Streaming & Execution

| Feature | Used in | Notes |
|---------|---------|-------|
| `InProcessExecution.StreamAsync` | JokeWriterHandler, StoryGeneratorHandler, tests | Primary execution model |
| `StreamingRun` / `WatchStreamAsync` | JokeWriterHandler, StoryGeneratorHandler, tests | Event-driven result collection |
| `WorkflowOutputEvent.As<T>()` | JokeWriterHandler, StoryGeneratorHandler | Typed output extraction |
| `AgentResponseEvent` | JokeWriterHandler, StoryGeneratorHandler | Agent response logging |
| `ExecutorInvokedEvent` | JokeWriterHandler | Input logging for executors |
| Agent streaming (`RunStreamingAsync`) | — | Stream individual agent responses |

### Hosting & APIs

| Feature | Used in | Notes |
|---------|---------|-------|
| OpenAI Responses API (`MapOpenAIResponses`) | Program.cs | `/responses` endpoint |
| OpenAI Conversations API (`MapOpenAIConversations`) | Program.cs | `/conversations` endpoint |
| DevUI (`AddDevUI` / `MapDevUI`) | Program.cs | `/devui` interactive dashboard |
| `.AddAsAIAgent()` | JokeWriter, StoryGenerator | Expose workflows as agents for DevUI/API |
| Azure Functions hosting | — | Durable Functions orchestration |
| A2A hosting | — | Agent-to-Agent communication |
| AG-UI server | — | SSE-based frontend protocol |

### Observability

| Feature | Used in | Notes |
|---------|---------|-------|
| OpenTelemetry tracing | Program.cs, JokeWriter agents | `AddSource` + OTLP exporter to Jaeger |
| `UseOpenTelemetry` (agent) | JokeWriter (concurrent agents) | Per-agent trace spans |
| `IChatClient` OTel wrapper | `ChatClientFactory.Create` | Traces LLM calls via `ChatClientBuilder` |
| Aspire Dashboard | — | Alternative to Jaeger |
| Application Insights | — | Azure cloud monitoring |

### LLM Providers

| Feature | Used in | Notes |
|---------|---------|-------|
| OpenAI | `ChatClientFactory` | Via `OpenAIClient` |
| GitHub Models | `ChatClientFactory` | Via OpenAI SDK with custom endpoint |
| Azure OpenAI | `ChatClientFactory` | Via `AzureOpenAIClient` |
| Anthropic | `ChatClientFactory` | Via `AnthropicClient` |
| Mock provider | `ChatClientFactory` | Canned responses for local dev |
| Ollama / ONNX (local models) | — | Local inference |
| Azure AI Foundry | — | Foundry agent support |

### Memory & Persistence

| Feature | Used in | Notes |
|---------|---------|-------|
| Chat history providers | — | In-memory, Cosmos DB |
| Chat reduction (summarization) | — | Manage context window size |
| Mem0 integration | — | External memory service |
| Cosmos DB checkpointing | — | Durable workflow state |

### Advanced

| Feature | Used in | Notes |
|---------|---------|-------|
| RAG (retrieval-augmented generation) | — | Vector store, text search |
| Multi-modal (image input/output) | — | Vision models |
| Extended thinking (Anthropic) | — | Reasoning traces |
| Code interpreter | — | Python execution in agents |
| Computer use | — | Vision-based interaction |
| Group chat | — | Multi-agent conversation |
| Semantic Kernel plugins | — | SK integration |

## Known .NET Gaps

Gaps discovered while building this project. Each entry links to the upstream issue and notes when it was last observed.

### DevUI

The DevUI frontend is shared between Python and .NET, but several features only work on the Python backend. Tracked in [#2084](https://github.com/microsoft/agent-framework/issues/2084).

| Gap | Details | Workaround | Last observed |
|-----|---------|------------|---------------|
| **Traces tab empty** | .NET DevUI hardcodes `capabilities["tracing"] = false`. The instrumentation pipeline only exists in Python. Confirmed by maintainer @victordibia. | Use Jaeger at `http://localhost:16686` or Aspire Dashboard | 2026-02-15 |
| **Tools tab empty** | Tool invocations (e.g. `GetDayOfYear`) are not surfaced. Depends on the same missing tracing infrastructure. See also [#2744](https://github.com/microsoft/agent-framework/issues/2744), [#3082](https://github.com/microsoft/agent-framework/issues/3082). | Add logging middleware (`UseToolInvocationLogging`) and watch console output | 2026-02-15 |
| **Inner agent details hidden** | When `ChatClientAgent` runs inside an executor via `BindAsExecutor`, DevUI shows only the final output — no tool calls, intermediate messages, or reasoning. | Check Jaeger traces for full call chain | 2026-02-15 |
| **Custom executors need Chat Protocol** | Non-agentic executors must handle `List<ChatMessage>` + `TurnToken` to work in DevUI, not just typed inputs. | Implement dual-protocol route handlers (see `JokeInputExecutor`, `StoryNameBridgeExecutor`) | 2026-02-15 |
| **Conversation memory not persisted** | Chat history / agent thread resets on each interaction. [#3484](https://github.com/microsoft/agent-framework/issues/3484) | None — each DevUI conversation starts fresh | 2026-02-15 |
