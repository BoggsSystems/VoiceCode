#!/bin/bash

# Test script to have Claude Code start a React frontend in a worker
# This simulates a real development task

ORCHESTRATOR_URL="https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${BLUE}=== VoiceCode React Frontend Test ===${NC}"
echo "This test will ask Claude Code to start a React development server"
echo ""

# Function to send a voice command
send_command() {
    local worker="$1"
    local command="$2"
    echo -e "${YELLOW}Worker $worker:${NC} $command"
    
    response=$(curl -s -X POST "$ORCHESTRATOR_URL/api/orchestrator/submit" \
        -H "Content-Type: application/json" \
        -d "{\"voiceCommand\": \"$command\"}" 2>&1)
    
    if echo "$response" | jq . >/dev/null 2>&1; then
        echo -e "${GREEN}Response:${NC}"
        echo "$response" | jq .
        
        # Extract task ID for monitoring
        task_id=$(echo "$response" | jq -r '.taskId // empty')
        if [ ! -z "$task_id" ]; then
            echo -e "${BLUE}Task ID: $task_id${NC}"
        fi
    else
        echo -e "${RED}Error:${NC} $response"
    fi
    echo ""
}

# Test 1: Check if worker 5 (e-commerce) has a React app
echo -e "${BLUE}Test 1: Check for React app in e-commerce platform${NC}"
send_command 5 "In worker 5, check if there's a React frontend in the repository and tell me about the project structure"

# Wait for worker to process
echo "Waiting 30 seconds for worker to process..."
sleep 30

# Test 2: Start the React development server
echo -e "${BLUE}Test 2: Start React development server${NC}"
send_command 5 "In worker 5, install dependencies and start the React development server. Show me what port it's running on."

# Test 3: Alternative - Create a simple React component
echo -e "${BLUE}Test 3: Create a React component${NC}"
send_command 5 "In worker 5, create a new React component called ProductCard that displays a product name, price, and an 'Add to Cart' button"

# Check worker status
echo -e "${BLUE}Checking worker-5 status...${NC}"
az containerapp revision list --name voicecode-worker-5 --resource-group voicecode-rg \
    --query "[0].{Replicas:properties.replicas, Status:properties.runningStatus, Created:properties.createdTime}" \
    --output table

# Check container logs (last 50 lines)
echo -e "${BLUE}Checking worker-5 logs...${NC}"
echo "Fetching recent container logs..."
az containerapp logs show \
    --name voicecode-worker-5 \
    --resource-group voicecode-rg \
    --tail 50 \
    --follow false 2>/dev/null || echo "Note: Logs might not be immediately available"

echo ""
echo -e "${YELLOW}Notes:${NC}"
echo "- Workers run in containers without exposed ports for development servers"
echo "- Claude Code will execute the commands but the React dev server won't be accessible externally"
echo "- To see the actual code changes, you'd need to check the git repository"
echo "- This tests Claude Code's ability to understand and execute development tasks"

echo -e "${BLUE}=== Test Complete ===${NC}"