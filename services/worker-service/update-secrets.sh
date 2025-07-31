#!/bin/bash

# Script to update Azure Container App secrets with actual values
# Replace the placeholder values with your actual secrets

echo "Updating Azure Container App secrets..."

# Update claude-api-key
az containerapp secret set \
  --name voicecode-worker \
  --resource-group voicecode \
  --secrets claude-api-key="YOUR_ACTUAL_CLAUDE_API_KEY" \
  --output none

echo "✓ Claude API Key updated"

# Update servicebus-connection-string
az containerapp secret set \
  --name voicecode-worker \
  --resource-group voicecode \
  --secrets servicebus-connection-string="YOUR_ACTUAL_SERVICEBUS_CONNECTION_STRING" \
  --output none

echo "✓ Service Bus Connection String updated"

# Update appinsights-connection-string
az containerapp secret set \
  --name voicecode-worker \
  --resource-group voicecode \
  --secrets appinsights-connection-string="YOUR_ACTUAL_APPINSIGHTS_CONNECTION_STRING" \
  --output none

echo "✓ Application Insights Connection String updated"

echo ""
echo "All secrets updated successfully!"
echo "The container app will automatically restart with the new configuration."