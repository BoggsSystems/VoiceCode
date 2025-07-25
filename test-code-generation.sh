#!/bin/bash

# Test script for code generation tasks that we can verify
# This simulates real development tasks

ORCHESTRATOR_URL="https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${BLUE}=== VoiceCode Code Generation Test ===${NC}"
echo "Testing various code generation tasks across different workers"
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
    else
        echo -e "${RED}Error:${NC} $response"
    fi
    echo ""
}

# Test different workers with specific tasks
echo -e "${BLUE}Test 1: Restaurant POS - Menu Management${NC}"
send_command 1 "In worker 1, create a new API endpoint for seasonal menu items with CRUD operations. Include fields for item name, description, price, and availability dates."

echo -e "${BLUE}Test 2: Law Firm - Document Generation${NC}"
send_command 2 "In worker 2, create a service class for generating legal document templates with placeholders for client information and case details."

echo -e "${BLUE}Test 3: E-commerce - Shopping Cart${NC}"
send_command 5 "In worker 5, add a discount calculation feature to the shopping cart that supports percentage and fixed amount discounts with validation."

echo -e "${BLUE}Test 4: Fitness App - Workout Tracking${NC}"
send_command 9 "In worker 9, create a workout history component that displays past workouts in a calendar view with exercise details."

echo -e "${BLUE}Test 5: Banking App - Security${NC}"
send_command 4 "In worker 4, implement a session timeout feature that warns users before logging them out for security."

# Check which workers are running
echo -e "${BLUE}Active Workers:${NC}"
az containerapp list --resource-group voicecode-rg \
    --query "[?starts_with(name, 'voicecode-worker-')].{Name:name, Replicas:properties.runningRevisions[0].replicas}" \
    --output table | grep -v " 0$" | grep -v "^$"

echo ""
echo -e "${YELLOW}What's happening:${NC}"
echo "- Each worker has Claude Code CLI installed"
echo "- Workers clone their assigned repository on startup"
echo "- Claude Code processes the voice commands autonomously"
echo "- Code changes would be made in the cloned repository"
echo "- To see results, you'd need to check the worker's git repository"

echo -e "${BLUE}=== Test Complete ===${NC}"