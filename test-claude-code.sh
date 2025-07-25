#!/bin/bash

# Test Claude Code functionality

ORCHESTRATOR_URL="https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${BLUE}=== Testing Claude Code Integration ===${NC}"
echo ""

# Send a test command
echo -e "${YELLOW}Sending command to worker-1...${NC}"
response=$(curl -s -X POST "$ORCHESTRATOR_URL/api/orchestrator/submit" \
    -H "Content-Type: application/json" \
    -d '{"voiceCommand": "In worker 1, list the main files and directories in the project and create a simple README.md file that describes what Create React App is"}')

echo "$response" | jq .

# Wait for processing
echo -e "${BLUE}Waiting 30 seconds for Claude Code to process...${NC}"
sleep 30

# Check logs
echo -e "${BLUE}Checking worker-1 logs for Claude Code activity...${NC}"
az containerapp logs show \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --tail 100 | grep -E "(MCP|Claude|Processing|claude|README)" | tail -30

# Check queue status
echo -e "${BLUE}Queue message counts:${NC}"
echo -n "worker-1-tasks: "
az servicebus queue show --name worker-1-tasks --namespace-name voicecodebus-0724 --resource-group voicecode-rg --query "countDetails.activeMessageCount" -o tsv
echo -n "worker-tasks: "
az servicebus queue show --name worker-tasks --namespace-name voicecodebus-0724 --resource-group voicecode-rg --query "countDetails.activeMessageCount" -o tsv

echo -e "${BLUE}=== Test Complete ===${NC}"