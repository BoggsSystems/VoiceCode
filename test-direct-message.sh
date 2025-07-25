#!/bin/bash

# Send a test message directly to the worker-tasks queue

ORCHESTRATOR_URL="https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${BLUE}=== Direct Worker Test ===${NC}"
echo ""

# First, let's update the orchestrator to send to worker-tasks instead
echo -e "${YELLOW}Sending test command that will go to worker-tasks queue...${NC}"

# Send a simple command without worker number (will fail but that's ok)
response=$(curl -s -X POST "$ORCHESTRATOR_URL/api/orchestrator/submit" \
    -H "Content-Type: application/json" \
    -d '{"voiceCommand": "List the files in the current directory"}')

echo "$response" | jq .

# Wait for processing
echo -e "${BLUE}Waiting 20 seconds for worker to process...${NC}"
sleep 20

# Check worker logs
echo -e "${BLUE}Checking worker-1 logs for processing activity...${NC}"
az containerapp logs show \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --tail 50 | grep -E "(Processing|Received|Execute|worker-tasks|Success)" | tail -20

# Check queue status
echo -e "${BLUE}Checking queue message counts...${NC}"
echo "worker-tasks queue:"
az servicebus queue show \
    --name worker-tasks \
    --namespace-name voicecodebus-0724 \
    --resource-group voicecode-rg \
    --query "countDetails.activeMessageCount" -o tsv

echo -e "${BLUE}=== Test Complete ===${NC}"