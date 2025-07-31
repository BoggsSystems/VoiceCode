#!/bin/bash

# Test end-to-end Claude Code SDK integration

echo "Testing Claude Code SDK Integration..."

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Test 1: Check SDK availability
echo -e "\n${YELLOW}Test 1: Checking SDK availability...${NC}"
curl -s http://localhost:5000/api/claudecodesdk/health | jq .
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ SDK health check passed${NC}"
else
    echo -e "${RED}✗ SDK health check failed${NC}"
fi

# Test 2: Test simple SDK execution
echo -e "\n${YELLOW}Test 2: Testing simple SDK execution...${NC}"
curl -X POST http://localhost:5000/api/claudecodesdk/execute \
    -H "Content-Type: application/json" \
    -d '{
        "prompt": "Create a simple hello world React component",
        "workingDirectory": "/tmp/test-sdk"
    }' | jq .

# Test 3: Test streaming SDK execution
echo -e "\n${YELLOW}Test 3: Testing streaming SDK execution...${NC}"
curl -X POST http://localhost:5000/api/claudecodesdk/execute/streaming \
    -H "Content-Type: application/json" \
    -d '{
        "prompt": "Build a todo list component with add and delete functionality",
        "workingDirectory": "/tmp/test-sdk-streaming"
    }'

# Test 4: Test worker with SDK command
echo -e "\n${YELLOW}Test 4: Testing worker with SDK-triggering command...${NC}"
curl -X POST http://localhost:5000/api/worker/test \
    -H "Content-Type: application/json" \
    -d '{
        "taskId": "test-sdk-'$(date +%s)'",
        "command": "Create a login form component with email and password fields",
        "sessionId": "test-session-123",
        "useSdk": true
    }' | jq .

# Test 5: Check if SDK streams are being published
echo -e "\n${YELLOW}Test 5: Checking Service Bus for SDK streams...${NC}"
# This would require Azure CLI or Service Bus Explorer
echo "Note: Check Azure Service Bus Explorer for messages in 'sdk-streams' topic"

echo -e "\n${YELLOW}Test Summary:${NC}"
echo "1. SDK availability - Check if SDK service is running"
echo "2. Simple execution - Test basic SDK functionality"
echo "3. Streaming execution - Test real-time progress updates"
echo "4. Worker integration - Test full pipeline with SDK"
echo "5. Observer streams - Verify narration generation"

echo -e "\n${GREEN}Integration test completed!${NC}"