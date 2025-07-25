#!/bin/bash

# VoiceCode Container Instances Restart Script
# Restarts containers to pick up new configuration

set -e

# Configuration
RESOURCE_GROUP="voicecode-dev-eus-rg"
SERVICES=("claude" "stt" "tts" "dispatcher" "generator" "router")

echo "========================================="
echo "Restarting VoiceCode Container Instances"
echo "========================================="

# Restart each service
for SERVICE in "${SERVICES[@]}"; do
    CONTAINER_NAME="voicecode-dev-eus-${SERVICE}-ci"
    
    echo ""
    echo "Restarting $SERVICE service..."
    
    # Restart the container
    az container restart \
        --resource-group $RESOURCE_GROUP \
        --name $CONTAINER_NAME \
        --output none
    
    echo "✓ $SERVICE service restart initiated"
done

echo ""
echo "All restart commands issued. Waiting for containers to come back online..."
sleep 60

# Check status
echo ""
echo "Checking container status..."
for SERVICE in "${SERVICES[@]}"; do
    CONTAINER_NAME="voicecode-dev-eus-${SERVICE}-ci"
    STATUS=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query instanceView.state -o tsv)
    echo "$SERVICE: $STATUS"
done

# Test health endpoints
echo ""
echo "Testing health endpoints..."
for SERVICE in "${SERVICES[@]}"; do
    CONTAINER_NAME="voicecode-dev-eus-${SERVICE}-ci"
    FQDN=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query ipAddress.fqdn -o tsv 2>/dev/null || echo "")
    if [ ! -z "$FQDN" ]; then
        echo -n "$SERVICE health: "
        curl -s -o /dev/null -w '%{http_code}\n' "http://$FQDN/health" 2>/dev/null || echo "Failed"
    fi
done

echo ""
echo "Restart complete!"