#!/bin/bash

# Test script for VoiceCode Orchestrator
# Simulates voice commands being sent to the orchestrator

ORCHESTRATOR_URL="https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"
SESSION_ID="test-session-$(date +%s)"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${BLUE}=== VoiceCode Orchestrator Test ===${NC}"
echo "Orchestrator URL: $ORCHESTRATOR_URL"
echo "Session ID: $SESSION_ID"
echo ""

# Function to send a voice command
send_command() {
    local command="$1"
    echo -e "${YELLOW}Sending command:${NC} $command"
    
    response=$(curl -s -X POST "$ORCHESTRATOR_URL/api/orchestrator/submit" \
        -H "Content-Type: application/json" \
        -d "{\"voiceCommand\": \"$command\", \"sessionId\": \"$SESSION_ID\"}")
    
    echo -e "${GREEN}Response:${NC}"
    echo "$response" | jq .
    echo ""
}

# Function to check context
check_context() {
    echo -e "${YELLOW}Checking current context:${NC}"
    curl -s "$ORCHESTRATOR_URL/api/orchestrator/context" \
        -H "Cookie: AspNetCore.Session=$SESSION_ID" | jq .
    echo ""
}

# Test 1: Explicit worker reference
echo -e "${BLUE}Test 1: Explicit worker reference${NC}"
send_command "Create a new API endpoint in worker 2 for user authentication"

# Test 2: Context-based routing (should go to worker 2)
echo -e "${BLUE}Test 2: Context-based routing${NC}"
send_command "Add validation to ensure email addresses are properly formatted"

# Test 3: Switch to different worker
echo -e "${BLUE}Test 3: Switch to different worker${NC}"
send_command "Show me the shopping cart implementation in worker 5"

# Test 4: Continue with context (should go to worker 5)
echo -e "${BLUE}Test 4: Continue with context${NC}"
send_command "Add a discount calculation feature"

# Test 5: No worker specified, no context
echo -e "${BLUE}Test 5: Clear context and try command without worker${NC}"
curl -s -X POST "$ORCHESTRATOR_URL/api/orchestrator/context/clear" \
    -H "Cookie: AspNetCore.Session=$SESSION_ID" | jq .
send_command "Fix the bug in the login form"

# Check final context
check_context

echo -e "${BLUE}=== Test Complete ===${NC}"