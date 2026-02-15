# CLAUDE.md — AgentLearn

A .NET 10 web API for learning Microsoft Agent Framework features: typed workflows, HITL (human-in-the-loop), fan-out/fan-in concurrency, and OpenTelemetry tracing.

## Project Structure

```
AgentLearn/
├── Controllers/            # TaskController — POST /task endpoint
├── Extensions/             # AIAgentBuilder pipeline helpers
├── Models/                 # DTOs: TaskRequest, TaskResponse, AgentMode, TaskKind, LlmOptions
├── Services/               # ITaskHandler, ChatClientFactory
├── TaskKinds/
│   ├── JokeWriter/         # JokeWriter.cs — models, executors, agents, handler, DI registration
│   └── StoryGenerator/     # StoryGenerator.cs — models, executors, agents, handler, DI registration
├── tests/
│   └── integration/        # MSTest integration tests with Moq
├── Program.cs              # DI wiring, OpenTelemetry, DevUI, OpenAI-compatible endpoints
└── run.sh                  # Starts Jaeger + runs server
```

Each TaskKind is self-contained in a single file: models, executors, agent factories, workflow builders, handler, and DI registration.

## Build & Test

```bash
# Run the server
cd dotnet/learn/AgentLearn
export GH_PAT=your_github_pat
dotnet run

# Run integration tests
cd dotnet/learn/AgentLearn/tests/integration
dotnet test --verbosity normal
```

## Code Style

- **No `var`** — always use explicit types
- **Primary constructors** — prefer `class Foo(ILogger logger)` over constructor bodies
- **Collection expressions** — use `[]` not `new List<T>()`
- **File-scoped namespaces** — `namespace Foo;`
- **No copyright headers** — do not add `// Copyright (c) Microsoft. All rights reserved.` to files in this project
- **`this.` for members** — use `this.` when accessing instance fields/properties
- **XML docs on all public APIs** — document every public class, method, and property; prefer `/// <inheritdoc />` when implementing an interface or overriding a base member
- **Suppress CA2007** — `ConfigureAwait` is unnecessary in ASP.NET Core; suppressed in csproj

## Testing Conventions

- **MSTest** — use `[TestClass]`, `[TestMethod]`, `Assert.*` (not xUnit)
- **Moq** for `IChatClient` mocking — use `Mock<IChatClient>` with delegate-based response generators
- **Key discovery**: `ChatClientAgent` passes system instructions via `ChatOptions.Instructions`, not as system messages in the message list
- **Key discovery**: the framework calls `GetStreamingResponseAsync` (not `GetResponseAsync`) for agent invocations
- **Timeout** — add `[Timeout(30_000)]` on workflow tests to catch hangs
- **NullLoggerFactory.Instance** for `ILoggerFactory` in tests

## Architecture Notes

### Task Kinds
- **JokeWriter** — three workflow modes: Single (Writer), Sequential (Writer -> Critic -> Editor), Concurrent (fan-out 3 pipelines -> Aggregator -> Selector)
- **StoryGenerator** — Single mode only with HITL: `RequestPort<string, string>` pauses for a character name, then Storyteller generates a story

### Executors
Custom executors bridge typed inputs/outputs with the ChatProtocol:
- `JokeInputExecutor` — dual-protocol: handles both `JokeRequest` (typed API) and `List<ChatMessage>` (DevUI)
- `JokeOutputExecutor` / `StoryOutputExecutor` — extract last assistant message, yield both typed output and chat messages
- `JokeAggregatorExecutor` — fan-in: accumulates N pipeline results, formats selection prompt
- `StoryNameBridgeExecutor` — converts HITL string response into ChatMessage + TurnToken

### RouteBuilder Ambiguity
`RouteBuilder` is ambiguous between `Microsoft.AspNetCore.Routing.RouteBuilder` and `Microsoft.Agents.AI.Workflows.RouteBuilder`. Fix with:
```csharp
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;
```

### Workflow Execution Pattern
```csharp
await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input);
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case WorkflowOutputEvent output:
            MyType? result = output.As<MyType>();
            break;
        case RequestInfoEvent requestInfo:  // HITL
            await run.SendResponseAsync(requestInfo.Request.CreateResponse(value));
            break;
    }
}
```
