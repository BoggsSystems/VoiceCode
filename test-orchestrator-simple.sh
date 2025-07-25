#!/bin/bash

# Simple test script for VoiceCode Orchestrator
# Tests basic voice command routing

ORCHESTRATOR_URL="https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${BLUE}=== VoiceCode Orchestrator Simple Test ===${NC}"
echo "Orchestrator URL: $ORCHESTRATOR_URL"
echo ""

# Function to send a voice command
send_command() {
    local command="$1"
    echo -e "${YELLOW}Sending:${NC} $command"
    
    response=$(curl -s -X POST "$ORCHESTRATOR_URL/api/orchestrator/submit" \
        -H "Content-Type: application/json" \
        -d "{\"voiceCommand\": \"$command\"}" 2>&1)
    
    # Check if response is JSON
    if echo "$response" | jq . >/dev/null 2>&1; then
        echo -e "${GREEN}Response:${NC}"
        echo "$response" | jq .
    else
        echo -e "${RED}Error:${NC} $response"
    fi
    echo ""
}

# Test different worker commands
echo -e "${BLUE}Test 1: Restaurant POS (Worker 1)${NC}"
send_command "Add a new menu item in worker 1 for a seasonal special"

echo -e "${BLUE}Test 2: Law Firm (Worker 2)${NC}"
send_command "Create a case summary template in worker 2"

echo -e "${BLUE}Test 3: E-commerce (Worker 5)${NC}"
send_command "Fix the shopping cart bug in worker 5 where items disappear"

echo -e "${BLUE}Test 4: Fitness App (Worker 9)${NC}"
send_command "Add a workout history feature to worker 9"

echo -e "${BLUE}Test 5: No worker specified${NC}"
send_command "Generate a report of all transactions"

# Check if workers are scaling up
echo -e "${BLUE}Checking worker status...${NC}"
echo "Waiting 10 seconds for workers to potentially scale up..."
sleep 10

echo -e "${YELLOW}Worker replicas:${NC}"
az containerapp list --resource-group voicecode-rg \
    --query "[?starts_with(name, 'voicecode-worker-')].{Name:name, Replicas:properties.runningRevisions[0].replicas}" \
    --output table 2>/dev/null | grep -E "worker-(1|2|5|9)"

echo -e "${BLUE}=== Test Complete ===${NC}"