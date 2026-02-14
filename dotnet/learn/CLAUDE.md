# CLAUDE.md

This folder contains **AgentLearn**, a .NET 10 web API project for experimenting with Microsoft Agent Framework features using GitHub Models as the LLM backend.

## Project Purpose

This is a learning/sandbox environment for exploring:
- Microsoft Agent Framework (`Microsoft.Agents.AI`)
- AI agent workflows (sequential, concurrent)
- OpenTelemetry tracing for AI operations
- GitHub Models integration via OpenAI SDK

## Project Structure

```
dotnet/learn/
├── AgentLearn/
│   ├── Controllers/       # API endpoints (TaskController)
│   ├── Models/            # Request/response DTOs, enums (AgentMode, TaskKind)
│   ├── Services/
│   │   ├── Agents/        # Agent definitions (JokeAgents.cs)
│   │   ├── ITaskHandler.cs
│   │   └── JokeWriterHandler.cs
│   ├── Program.cs         # DI setup, OpenTelemetry config, ChatClient registration
│   └── run.sh             # Starts Jaeger + runs server
├── Directory.Build.props
├── Directory.Packages.props
└── README.md
```

## Key Packages

- `Microsoft.Agents.AI` - Core agent abstractions (`AIAgent`, `AgentResponse`)
- `Microsoft.Agents.AI.OpenAI` - OpenAI/GitHub Models integration
- `Microsoft.Agents.AI.Workflows` - Workflow orchestration (`Workflow`, `AgentWorkflowBuilder`, `Run`)
- `Microsoft.Extensions.AI` - `IChatClient` abstraction

## Running the Project

```bash
cd AgentLearn
export GH_PAT=your_github_pat
dotnet run           # API at https://localhost:5001
./run.sh             # Also starts Jaeger for tracing
```

## API Usage

```bash
curl -X POST https://localhost:5001/task \
  -H "Content-Type: application/json" \
  -d '{"task": "Tell me a joke", "mode": "Single", "kind": "JokeWriter"}'
```

Modes: `Single` (one agent), `Sequential` (writer -> critic -> editor), `Concurrent` (parallel agents + selector)

## Code Style Rules

- **Avoid using `var`** - Always use explicit types for variable declarations
- **Always use primary constructors** - Prefer primary constructor syntax over traditional constructors
- **Use simple collection init** - Use collection expressions like `[]` instead of `new List<T>()`
- **File-scoped namespaces** - Use `namespace Foo;` instead of `namespace Foo { }`
- **Copyright header** - All files start with `// Copyright (c) Microsoft. All rights reserved.`

## Key Patterns

### Agent Creation
Agents are created via factory methods in `Services/Agents/JokeAgents.cs` using `AIAgent.Create()` with system prompts.

### Workflow Execution
```csharp
Workflow workflow = AgentWorkflowBuilder.BuildSequential([agent1, agent2]);
await using Run run = await InProcessExecution.RunAsync(workflow, messages);
await run.ResumeAsync<object>(default, new TurnToken(emitEvents: true));
WorkflowOutputEvent? output = run.OutgoingEvents.OfType<WorkflowOutputEvent>().FirstOrDefault();
```

### IChatClient Registration
The `IChatClient` is registered as a singleton with OpenTelemetry wrapping for tracing.
