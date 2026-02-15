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

## Known .NET DevUI Gaps

The DevUI frontend is shared between Python and .NET backends, but the .NET backend has several features that are not yet implemented. These are tracked upstream in [microsoft/agent-framework#2084](https://github.com/microsoft/agent-framework/issues/2084).

### Traces tab shows "No Data"

The DevUI Traces tab requires an instrumentation pipeline that only exists in the Python backend. The .NET DevUI hardcodes `capabilities["tracing"] = false` in `MetaApiExtensions.cs`. OTel traces are collected and exported (to Jaeger via OTLP), but DevUI cannot display them inline. Use Jaeger at `http://localhost:16686` instead.

- Confirmed by maintainer @victordibia: *"Currently this has been tested mostly in python. There is some work to be done to ensure OTel works for .NET backend."*

### Tools tab shows "No Data"

Tool invocations within agents (e.g. `GetDayOfYear`, `GetLuckyNumber`) are not surfaced in the DevUI Tools tab. The tab depends on the same tracing infrastructure as the Traces tab. Tool calls *do* execute and appear in Jaeger traces, but DevUI has no visibility into them on .NET.

- See also [#2744](https://github.com/microsoft/agent-framework/issues/2744) — tool call rendering in non-streaming mode
- See also [#3082](https://github.com/microsoft/agent-framework/issues/3082) — tool calls not shown even in Python streaming mode

### Inner agent details not visible

When an agent runs inside an executor (e.g. `ChatClientAgent` bound via `BindAsExecutor`), DevUI does not show the agent's internal tool calls, intermediate messages, or reasoning. Only the final executor output is visible. This is called out explicitly in [#2084](https://github.com/microsoft/agent-framework/issues/2084).

### Custom executors require Chat Protocol support

Non-agentic executors must handle both `List<ChatMessage>` and `TurnToken` inputs to work in DevUI. Without this dual-protocol support, DevUI cannot drive the executor. This is why `JokeInputExecutor` and `StoryNameBridgeExecutor` accept both typed inputs and chat messages.

### Conversation memory not persisted

DevUI does not maintain chat history / agent thread across interactions ([#3484](https://github.com/microsoft/agent-framework/issues/3484)). Each conversation starts fresh.

### Workarounds

- **OTel traces**: Use Jaeger (`./run.sh` starts it automatically) or Aspire Dashboard
- **Tool call visibility**: Add logging middleware (see `UseToolInvocationLogging` in `Extensions/`) and watch server console output
- **Executor compatibility**: Implement dual-protocol handlers (`List<ChatMessage>` + `TurnToken`) in custom executors
