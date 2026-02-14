# AgentLearn

A .NET Core web API project for experimenting with Microsoft Agent Framework features using GitHub Models as the LLM backend.

## Prerequisites

- .NET 10.0 SDK
- GitHub Token with access to GitHub Models
- Docker (optional, for Jaeger tracing)

## Configuration

Set the GitHub PAT via environment variable or configuration:

```bash
export GH_PAT=your_github_pat_here
```

Or in `appsettings.json`:
```json
{
  "GH_PAT": "your_github_pat_here"
}
```

## Running the Project

```bash
cd AgentLearn
dotnet run
```

Or use the convenience script that sets up Jaeger and runs the server:

```bash
cd AgentLearn
./run.sh
```

The server starts at `https://localhost:5001` (or `http://localhost:5000`).

## API Endpoints

### POST /task

Accepts a task request with the following JSON body:

```json
{
  "task": "Tell me a joke about programming",
  "mode": "Single",
  "kind": "JokeWriter"
}
```

**Fields:**
- `task` - The task description (string)
- `mode` - Agent workflow mode: `Single` or `Sequential`
- `kind` - Type of task handler: `JokeWriter`

**Example:**

```bash
curl -X POST https://localhost:5001/task \
  -H "Content-Type: application/json" \
  -d '{"task": "Tell me a joke about programming", "mode": "Single", "kind": "JokeWriter"}'
```

**Response:**

```json
{
  "task": "Tell me a joke about programming",
  "mode": "Single",
  "kind": "JokeWriter",
  "result": "Why do programmers prefer dark mode? Because light attracts bugs!"
}
```

## Project Structure

```
AgentLearn/
├── Controllers/
│   └── TaskController.cs
├── Models/
│   ├── AgentMode.cs
│   ├── TaskKind.cs
│   ├── TaskRequest.cs
│   └── TaskResponse.cs
├── Services/
│   ├── ITaskHandler.cs
│   └── JokeWriterHandler.cs
└── Program.cs
```

## Packages

- `Microsoft.Agents.AI` - Core agent abstractions
- `Microsoft.Agents.AI.OpenAI` - OpenAI/GitHub Models integration
- `Microsoft.Agents.AI.Workflows` - Workflow orchestration
- `OpenTelemetry.*` - Distributed tracing

## OpenTelemetry with Jaeger

The project is configured to export traces to Jaeger via OTLP.

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
