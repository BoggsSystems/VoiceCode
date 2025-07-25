#!/bin/bash

# Test with a public repository that doesn't require authentication

ORCHESTRATOR_URL="https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${BLUE}=== Testing Worker with Public Repository ===${NC}"
echo ""

# First, update worker-1 to use a public repo (React starter)
echo -e "${YELLOW}Updating worker-1 to use public Create React App template...${NC}"
az containerapp update \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --set-env-vars \
        ASSIGNED_REPO="https://github.com/facebook/create-react-app.git" \
        ServiceBus__ConnectionString="secretref:servicebus-connection" \
        Mcp__Enabled="false" \
    --output none

echo "Waiting 30 seconds for worker to restart..."
sleep 30

# Send a simple test command
echo -e "${BLUE}Sending test command to worker-1...${NC}"
response=$(curl -s -X POST "$ORCHESTRATOR_URL/api/orchestrator/submit" \
    -H "Content-Type: application/json" \
    -d '{"voiceCommand": "In worker 1, list the main directories and tell me what kind of project this is"}')

echo -e "${GREEN}Response:${NC}"
echo "$response" | jq .

# Check worker logs
echo -e "${BLUE}Checking worker-1 logs...${NC}"
az containerapp logs show \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --tail 30 | grep -E "(Repository|Clone|INF|ERR)" | tail -20

echo -e "${BLUE}=== Test Complete ===${NC}"