#!/bin/bash

# Verify VoiceCode SDK Integration Deployment

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

RESOURCE_GROUP="voicecode-rg"

echo -e "${BLUE}VoiceCode Deployment Verification${NC}"
echo -e "${BLUE}=================================${NC}"

# Function to check service status
check_service() {
    local service_name=$1
    local url=$2
    
    echo -e "\n${YELLOW}Checking $service_name...${NC}"
    
    # Check if container app exists
    STATUS=$(az containerapp show --name $service_name --resource-group $RESOURCE_GROUP --query "properties.runningStatus" -o tsv 2>/dev/null || echo "NOT_FOUND")
    
    if [ "$STATUS" == "Running" ]; then
        echo -e "${GREEN}✓ $service_name is running${NC}"
        
        # Check health endpoint if URL provided
        if [ ! -z "$url" ]; then
            HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" https://$url/health 2>/dev/null || echo "000")
            if [ "$HEALTH_STATUS" == "200" ]; then
                echo -e "${GREEN}✓ Health check passed${NC}"
            else
                echo -e "${RED}✗ Health check failed (HTTP $HEALTH_STATUS)${NC}"
            fi
        fi
        
        # Show recent logs
        echo "Recent logs:"
        az containerapp logs show --name $service_name --resource-group $RESOURCE_GROUP --tail 5 2>/dev/null || echo "Unable to fetch logs"
    else
        echo -e "${RED}✗ $service_name status: $STATUS${NC}"
    fi
}

# Check Observer Service
OBSERVER_URL=$(az containerapp show --name voicecode-observer --resource-group $RESOURCE_GROUP --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || echo "")
check_service "voicecode-observer" "$OBSERVER_URL"

# Check Worker Services
for i in {1..3}; do
    check_service "voicecode-worker-$i" ""
done

# Check Service Bus
echo -e "\n${YELLOW}Checking Service Bus resources...${NC}"
NAMESPACE="voicecode-servicebus"

# Check topic
TOPIC_EXISTS=$(az servicebus topic show --name sdk-streams --namespace-name $NAMESPACE --resource-group $RESOURCE_GROUP --query name -o tsv 2>/dev/null || echo "")
if [ ! -z "$TOPIC_EXISTS" ]; then
    echo -e "${GREEN}✓ Topic 'sdk-streams' exists${NC}"
else
    echo -e "${RED}✗ Topic 'sdk-streams' not found${NC}"
fi

# Check subscription
SUB_EXISTS=$(az servicebus topic subscription show --name observer --topic-name sdk-streams --namespace-name $NAMESPACE --resource-group $RESOURCE_GROUP --query name -o tsv 2>/dev/null || echo "")
if [ ! -z "$SUB_EXISTS" ]; then
    echo -e "${GREEN}✓ Subscription 'observer' exists${NC}"
else
    echo -e "${RED}✗ Subscription 'observer' not found${NC}"
fi

# Check environment variables
echo -e "\n${YELLOW}Checking Worker 1 configuration...${NC}"
SDK_ENABLED=$(az containerapp show --name voicecode-worker-1 --resource-group $RESOURCE_GROUP --query "properties.template.containers[0].env[?name=='ClaudeCodeSdk__Enabled'].value | [0]" -o tsv 2>/dev/null || echo "false")
STREAM_TOPIC=$(az containerapp show --name voicecode-worker-1 --resource-group $RESOURCE_GROUP --query "properties.template.containers[0].env[?name=='Worker__StreamTopicName'].value | [0]" -o tsv 2>/dev/null || echo "")

echo "Claude Code SDK Enabled: $SDK_ENABLED"
echo "Stream Topic Name: $STREAM_TOPIC"

# Summary
echo -e "\n${BLUE}========================================${NC}"
echo -e "${BLUE}Deployment Summary${NC}"
echo -e "${BLUE}========================================${NC}"

if [ ! -z "$OBSERVER_URL" ]; then
    echo -e "\nObserver Service URL:"
    echo -e "${GREEN}https://$OBSERVER_URL${NC}"
    echo -e "\nTest endpoints:"
    echo "- Health: curl https://$OBSERVER_URL/health"
    echo "- Status: curl https://$OBSERVER_URL/api/health/status"
fi

echo -e "\n${YELLOW}Quick Tests:${NC}"
echo "1. Send a test voice command:"
echo "   'Worker 1, create a simple React button component'"
echo ""
echo "2. Monitor Observer logs:"
echo "   az containerapp logs tail --name voicecode-observer --resource-group $RESOURCE_GROUP --follow"
echo ""
echo "3. Monitor Worker logs:"
echo "   az containerapp logs tail --name voicecode-worker-1 --resource-group $RESOURCE_GROUP --follow"

echo -e "\n${GREEN}Verification complete!${NC}"