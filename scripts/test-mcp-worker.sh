#!/bin/bash

# Test script for MCP Worker setup
# This script helps test the Claude Code MCP integration locally

set -e

echo "=== VoiceCode MCP Worker Test Script ==="
echo

# Check if Claude Code is installed
if ! command -v claude-code &> /dev/null; then
    echo "❌ Claude Code CLI not found. Please install it first:"
    echo "   curl -fsSL https://storage.googleapis.com/anthropic-public/claude-code/install.sh | sh"
    exit 1
fi

echo "✅ Claude Code CLI found"

# Check if API key is set
if [ -z "$ANTHROPIC_API_KEY" ]; then
    echo "❌ ANTHROPIC_API_KEY environment variable not set"
    echo "   Please set: export ANTHROPIC_API_KEY=your-api-key"
    exit 1
fi

echo "✅ API key configured"

# Create test workspace
TEST_WORKSPACE="/tmp/voicecode-test-workspace"
mkdir -p $TEST_WORKSPACE
echo "✅ Created test workspace: $TEST_WORKSPACE"

# Start worker service
echo
echo "Starting Worker Service..."
cd services/worker-service
dotnet run &
WORKER_PID=$!
echo "Worker Service PID: $WORKER_PID"

# Wait for service to start
echo "Waiting for service to start..."
sleep 10

# Test health endpoint
echo
echo "Testing health endpoint..."
HEALTH_RESPONSE=$(curl -s http://localhost:5006/health || echo "Failed")
echo "Health response: $HEALTH_RESPONSE"

# Test MCP connection
echo
echo "Testing MCP connection..."
TOOLS_RESPONSE=$(curl -s http://localhost:5006/api/worker/tools || echo "Failed")
echo "Available tools: $TOOLS_RESPONSE"

# Test simple task execution
echo
echo "Testing task execution..."
TASK_JSON='{
  "type": "file_operation",
  "description": "Create a test file",
  "workspaceId": "test-workspace",
  "parameters": {
    "operation": "write",
    "path": "/tmp/voicecode-test-workspace/test.txt",
    "content": "Hello from VoiceCode MCP Worker!"
  }
}'

TASK_RESPONSE=$(curl -s -X POST http://localhost:5006/api/worker/execute \
  -H "Content-Type: application/json" \
  -d "$TASK_JSON" || echo "Failed")

echo "Task response: $TASK_RESPONSE"

# Check if file was created
echo
if [ -f "$TEST_WORKSPACE/test.txt" ]; then
    echo "✅ Test file created successfully"
    echo "File content: $(cat $TEST_WORKSPACE/test.txt)"
else
    echo "❌ Test file not created"
fi

# Cleanup
echo
echo "Cleaning up..."
kill $WORKER_PID 2>/dev/null || true
rm -rf $TEST_WORKSPACE

echo
echo "Test complete!"