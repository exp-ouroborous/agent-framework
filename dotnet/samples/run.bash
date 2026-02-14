#!/usr/bin/env bash

# Script to run a .NET project with environment variables loaded from .env file
# Usage: ./run.bash <path-to-csproj> [framework]

set -e

# Check if csproj path is provided
if [ -z "$1" ]; then
    echo "Error: No .csproj file specified"
    echo "Usage: $0 <path-to-csproj> [framework]"
    echo "Example: $0 GettingStarted/Agents/Agent_Step01_Running/Agent_Step01_Running.csproj"
    echo "Example: $0 GettingStarted/Agents/Agent_Step01_Running/Agent_Step01_Running.csproj net10.0"
    exit 1
fi

CSPROJ_PATH="$1"
FRAMEWORK="${2:-net10.0}"  # Default to net10.0 if not specified

# Check if csproj file exists
if [ ! -f "$CSPROJ_PATH" ]; then
    echo "Error: File not found: $CSPROJ_PATH"
    exit 1
fi

# Determine the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Path to the .env file (same directory as this script)
ENV_FILE="$SCRIPT_DIR/.env"

# Load environment variables from .env file if it exists
if [ -f "$ENV_FILE" ]; then
    echo "Loading environment variables from: $ENV_FILE"
    # Export variables from .env file, ignoring comments and empty lines
    set -a
    source <(grep -v '^#' "$ENV_FILE" | grep -v '^$' | sed 's/\r$//')
    set +a
    echo "Environment variables loaded successfully"
else
    echo "Warning: .env file not found at: $ENV_FILE"
    echo "Proceeding without loading environment variables"
fi

# Run the project
echo "Running: dotnet run --project $CSPROJ_PATH --framework $FRAMEWORK"
dotnet run --project "$CSPROJ_PATH" --framework "$FRAMEWORK"
