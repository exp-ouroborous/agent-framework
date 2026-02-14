# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Microsoft Agent Framework is a multi-language framework for building, orchestrating, and deploying AI agents. It provides both .NET (C#) and Python implementations with consistent APIs. The framework supports simple chat agents to complex multi-agent workflows with graph-based orchestration.

## Repository Structure

- `python/` - Python implementation (monorepo with 22+ sub-packages in `packages/`)
- `dotnet/` - .NET/C# implementation (`src/` for source, `tests/` for tests)
- `docs/decisions/` - Architectural Decision Records (ADRs)

## Build and Test Commands

### Python

All commands run from the `python/` directory using `uv run poe`:

```bash
# Setup
uv sync --dev                    # Install all dependencies
uv run poe setup -p 3.13         # Full setup with specific Python version
uv run poe pre-commit-install    # Install pre-commit hooks

# Code quality
uv run poe fmt                   # Format with ruff
uv run poe lint                  # Lint and fix
uv run poe pyright               # Type check with Pyright
uv run poe mypy                  # Type check with MyPy
uv run poe check                 # Run ALL checks (fmt, lint, type-check, tests)

# Testing
uv run poe test                  # Run tests with coverage (sequential)
uv run poe all-tests             # Parallel test execution
uv run poe all-tests-cov         # Parallel tests with coverage

# Run tests for a specific package
uv run --directory packages/core poe test
uv run --directory packages/azure-ai poe test
```

Integration tests require `RUN_INTEGRATION_TESTS=true` environment variable.

### .NET

Commands run from the `dotnet/` directory:

```bash
dotnet build                     # Build solution
dotnet test                      # Run all tests
dotnet format                    # Auto-fix formatting
```

.NET SDK version: 10.0 (configured in `global.json`)

## Architecture

### Python Package Structure

The Python codebase uses a modular subpackage design:

- **Core package** (`packages/core/`): Contains `agent_framework` with core abstractions
- **Provider packages** (`packages/azure-ai/`, `packages/anthropic/`, etc.): Separate installable packages for each LLM provider

**Lazy loading pattern**: Provider folders in core use `__getattr__` to lazy load from connector packages. Users import from consistent locations:

```python
from agent_framework import ChatAgent, tool
from agent_framework.openai import OpenAIChatClient
from agent_framework.azure import AzureOpenAIChatClient
```

### Key Abstractions

- **ChatAgent**: Main agent type for conversational AI with tool support
- **ChatClient**: Abstracted client interface for multiple providers
- **Workflows**: Graph-based orchestration with streaming, checkpointing, human-in-the-loop
- **Middleware**: Request/response processing pipeline

### .NET Structure

- Solution file: `agent-framework-dotnet.slnx`
- Central package management: `Directory.Packages.props`
- Namespace: `Microsoft.Agents.AI.*`

## Code Conventions

### Python

- Copyright: `# Copyright (c) Microsoft. All rights reserved.` at top of each file
- Line length: 120 characters
- Docstrings: Google-style for public APIs
- Async-first: Assume everything is asynchronous
- Logging: Use `from agent_framework import get_logger; logger = get_logger()`
- Type hints: Required throughout

### C#

- Copyright: `// Copyright (c) Microsoft. All rights reserved.` at top of each file
- XML documentation for all public APIs
- Explicit types (prefer over `var`)
- `Async` suffix on async methods
- Seal private classes that aren't subclassed
- Use `this.` for class member access

### Sample Code

- Configuration via environment variables (UPPER_SNAKE_CASE)
- No hardcoded secrets
- Single entry point (Program.cs or main())
- Include README.md in sample directories

## Pre-commit Hooks

Python pre-commit hooks run automatically and include:
- TOML/YAML/JSON validation
- Code formatting (ruff)
- Security scanning (Bandit)
- Type checking

Run manually: `uv run pre-commit run -a`

## Architectural Decision Records

Key ADRs in `docs/decisions/`:
- ADR-0002: Agent tools and function calling
- ADR-0003: OpenTelemetry instrumentation
- ADR-0005: Python naming conventions
- ADR-0008: Python subpackages design (modular architecture)
- ADR-0012: TypedDict options for configuration
- ADR-0014: Feature collections pattern
