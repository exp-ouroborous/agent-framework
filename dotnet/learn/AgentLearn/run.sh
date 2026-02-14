#!/bin/bash
# Copyright (c) Microsoft. All rights reserved.

set -e

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load environment variables from .env file if it exists
if [ -f "$SCRIPT_DIR/.env" ]; then
    echo "Loading environment variables from .env..."
    set -a
    source "$SCRIPT_DIR/.env"
    set +a
fi

echo "Setting up Jaeger..."

# Check if Jaeger container exists
if docker ps -a --format '{{.Names}}' | grep -q '^jaeger$'; then
    # Container exists, check if running
    if docker ps --format '{{.Names}}' | grep -q '^jaeger$'; then
        echo "Jaeger is already running"
    else
        echo "Starting existing Jaeger container..."
        docker start jaeger
    fi
else
    echo "Creating and starting Jaeger container..."
    docker run -d --name jaeger \
        -e COLLECTOR_OTLP_ENABLED=true \
        -p 16686:16686 \
        -p 4317:4317 \
        -p 4318:4318 \
        jaegertracing/all-in-one:latest
fi

echo "Jaeger UI available at http://localhost:16686"
echo ""
echo "Starting AgentLearn server..."

# Enable sensitive data capture in OTel traces (dev mode only)
export OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true

# Run the server
dotnet run --project "$SCRIPT_DIR"
