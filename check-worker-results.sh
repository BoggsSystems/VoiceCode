#!/bin/bash

# Script to check Service Bus queues for messages and results

RESOURCE_GROUP="voicecode-rg"
SERVICE_BUS="voicecodebus-0724"

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${BLUE}=== Checking VoiceCode Service Bus Queues ===${NC}"
echo ""

# Function to check queue messages
check_queue() {
    local queue_name="$1"
    echo -e "${YELLOW}Queue: $queue_name${NC}"
    
    # Get message count
    message_count=$(az servicebus queue show \
        --name "$queue_name" \
        --namespace-name $SERVICE_BUS \
        --resource-group $RESOURCE_GROUP \
        --query "countDetails.activeMessageCount" -o tsv 2>/dev/null || echo "0")
    
    if [ "$message_count" != "0" ] && [ ! -z "$message_count" ]; then
        echo -e "${GREEN}Active messages: $message_count${NC}"
    else
        echo "Active messages: 0"
    fi
    
    # Get dead letter count
    dead_letter_count=$(az servicebus queue show \
        --name "$queue_name" \
        --namespace-name $SERVICE_BUS \
        --resource-group $RESOURCE_GROUP \
        --query "countDetails.deadLetterMessageCount" -o tsv 2>/dev/null || echo "0")
    
    if [ "$dead_letter_count" != "0" ] && [ ! -z "$dead_letter_count" ]; then
        echo -e "${YELLOW}Dead letter messages: $dead_letter_count${NC}"
    fi
    echo ""
}

# Check worker task queues
echo -e "${BLUE}Worker Task Queues:${NC}"
for i in {1..10}; do
    check_queue "worker-$i-tasks"
done

# Check if worker-results queue exists
echo -e "${BLUE}Results Queue:${NC}"
check_queue "worker-results"

# Create the results queue if it doesn't exist
echo -e "${BLUE}Creating worker-results queue if needed...${NC}"
az servicebus queue create \
    --name worker-results \
    --namespace-name $SERVICE_BUS \
    --resource-group $RESOURCE_GROUP \
    --max-size 1024 \
    --default-message-time-to-live P7D \
    --output none 2>/dev/null && echo "Created worker-results queue" || echo "Queue already exists or error occurred"

echo ""
echo -e "${BLUE}Worker Container Status:${NC}"
az containerapp list --resource-group $RESOURCE_GROUP \
    --query "[?starts_with(name, 'voicecode-worker-')].{Name:name, Replicas:properties.runningRevisions[0].replicas}" \
    --output table | grep -v " 0$" | grep -v "^$"

echo -e "${BLUE}=== Complete ===${NC}"