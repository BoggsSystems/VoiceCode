#!/bin/bash

# VoiceCode Container Instances Update Script
# Updates existing containers with new configuration

set -e

# Configuration
RESOURCE_GROUP="voicecode-dev-eus-rg"
KEY_VAULT_NAME="voicecodedevkveus"
SERVICES=("claude" "stt" "tts" "dispatcher" "generator" "router")

# API Keys
CLAUDE_API_KEY="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"
OPENAI_API_KEY="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"

echo "========================================="
echo "Updating VoiceCode Container Instances"
echo "========================================="
echo "Resource Group: $RESOURCE_GROUP"
echo "Key Vault: $KEY_VAULT_NAME"
echo "========================================="

# Function to update container environment variables
update_container() {
    local SERVICE=$1
    local CONTAINER_NAME="voicecode-dev-eus-${SERVICE}-ci"
    
    echo ""
    echo "Updating $SERVICE service..."
    
    # Get container details
    CONTAINER_INFO=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --output json)
    
    if [ $? -ne 0 ]; then
        echo "ERROR: Container $CONTAINER_NAME not found"
        return 1
    fi
    
    # Extract current configuration
    IMAGE=$(echo $CONTAINER_INFO | jq -r '.containers[0].image')
    CPU=$(echo $CONTAINER_INFO | jq -r '.containers[0].resources.requests.cpu')
    MEMORY=$(echo $CONTAINER_INFO | jq -r '.containers[0].resources.requests.memoryInGB')
    
    echo "Current configuration:"
    echo "  Image: $IMAGE"
    echo "  CPU: $CPU"
    echo "  Memory: ${MEMORY}GB"
    
    # Update Key Vault secrets in the containers
    case $SERVICE in
        "claude")
            echo "Adding Claude API key to Key Vault..."
            az keyvault secret set --vault-name $KEY_VAULT_NAME --name "ClaudeApiKey" --value "$CLAUDE_API_KEY" --output none || true
            ;;
        "generator")
            echo "Adding OpenAI API key to Key Vault..."
            az keyvault secret set --vault-name $KEY_VAULT_NAME --name "OpenAIApiKey" --value "$OPENAI_API_KEY" --output none || true
            ;;
    esac
    
    # Check container status
    STATUS=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query instanceView.state -o tsv)
    echo "Container status: $STATUS"
    
    # Get logs
    echo "Recent logs:"
    az container logs --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --tail 10 || true
    
    echo "✓ $SERVICE service checked"
}

# Check each service
for SERVICE in "${SERVICES[@]}"; do
    update_container $SERVICE
done

echo ""
echo "========================================="
echo "Container status check complete"
echo "========================================="

# Show all containers
echo ""
echo "All containers:"
az container list --resource-group $RESOURCE_GROUP --output table

# Show FQDNs
echo ""
echo "Service URLs:"
for SERVICE in "${SERVICES[@]}"; do
    CONTAINER_NAME="voicecode-dev-eus-${SERVICE}-ci"
    FQDN=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query ipAddress.fqdn -o tsv 2>/dev/null || echo "Not found")
    echo "$SERVICE: https://$FQDN"
done

# Test health endpoints
echo ""
echo "Testing health endpoints..."
for SERVICE in "${SERVICES[@]}"; do
    CONTAINER_NAME="voicecode-dev-eus-${SERVICE}-ci"
    FQDN=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query ipAddress.fqdn -o tsv 2>/dev/null || echo "")
    if [ ! -z "$FQDN" ]; then
        HEALTH_STATUS=$(curl -s -o /dev/null -w '%{http_code}' "http://$FQDN/health" 2>/dev/null || echo "Failed")
        echo "$SERVICE health: $HEALTH_STATUS"
    fi
done