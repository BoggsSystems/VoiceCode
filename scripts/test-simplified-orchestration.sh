#!/bin/bash

# Test Simplified Orchestration (Phase 5)
# Tests voice task submission with Claude Code handling all planning

set -e

ORCHESTRATOR_URL="${ORCHESTRATOR_URL:-http://localhost:5010}"

echo "========================================="
echo "Testing Simplified Voice Orchestration"
echo "========================================="

# Function to submit voice task
submit_voice_task() {
    local prompt=$1
    local session_id=${2:-$(uuidgen)}
    
    echo ""
    echo "Submitting voice task: \"$prompt\""
    echo "Session ID: $session_id"
    
    RESPONSE=$(curl -s -X POST "$ORCHESTRATOR_URL/api/v2/orchestration/voice-task" \
        -H "Content-Type: application/json" \
        -d "{
            \"voicePrompt\": \"$prompt\",
            \"sessionId\": \"$session_id\"
        }")
    
    SUCCESS=$(echo $RESPONSE | jq -r '.success')
    TASK_ID=$(echo $RESPONSE | jq -r '.taskId')
    
    if [ "$SUCCESS" = "true" ]; then
        echo "✅ Task submitted successfully"
        echo "Task ID: $TASK_ID"
        echo ""
        echo "Voice Response:"
        echo $RESPONSE | jq -r '.voiceResponse'
        echo ""
        echo "Files Created:"
        echo $RESPONSE | jq -r '.filesCreated[]' 2>/dev/null || echo "None"
        echo ""
        echo "Duration: $(echo $RESPONSE | jq -r '.duration')"
    else
        echo "❌ Task failed"
        echo "Error: $(echo $RESPONSE | jq -r '.error')"
    fi
    
    echo "========================================="
}

# Test 1: Simple task
echo "Test 1: Simple Python Function"
submit_voice_task "Create a Python function that calculates the factorial of a number"

# Test 2: More complex task
echo ""
echo "Test 2: Complete Web Application"
submit_voice_task "Create a simple todo list web application using React and Express.js with a REST API"

# Test 3: Testing task
echo ""
echo "Test 3: Testing Task"
submit_voice_task "Write comprehensive unit tests for a calculator class that supports add, subtract, multiply, and divide operations"

# Test 4: Worker pool status
echo ""
echo "Test 4: Checking Worker Pool Status"
POOL_STATUS=$(curl -s "$ORCHESTRATOR_URL/api/v2/orchestration/workers/status")
echo "Worker Pool Status:"
echo $POOL_STATUS | jq '{
    totalWorkers: .totalWorkers,
    healthyWorkers: .healthyWorkers,
    availableWorkers: .availableWorkers,
    utilizationPercentage: .utilizationPercentage
}'

# Test 5: Health check
echo ""
echo "Test 5: Health Check"
HEALTH=$(curl -s "$ORCHESTRATOR_URL/api/v2/orchestration/health")
echo $HEALTH | jq '.'

echo ""
echo "========================================="
echo "Simplified Orchestration Test Complete"
echo "========================================="
echo ""
echo "Key Benefits of Simplified Model:"
echo "✅ Claude Code handles all task planning internally"
echo "✅ No complex phase management in orchestrator"
echo "✅ Simpler code with fewer moving parts"
echo "✅ Voice-friendly responses"
echo "✅ Real-time progress updates via SignalR"