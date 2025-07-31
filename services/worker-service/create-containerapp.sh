#!/bin/bash

# Create Container App with managed identity
az containerapp create \
  --name voicecode-worker \
  --resource-group voicecode \
  --environment voicecode-env \
  --image voicecodebuildsprod.azurecr.io/voicecode-worker:latest \
  --target-port 80 \
  --ingress external \
  --cpu 1 \
  --memory 2 \
  --min-replicas 0 \
  --max-replicas 1 \
  --user-assigned /subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourcegroups/voicecode/providers/Microsoft.ManagedIdentity/userAssignedIdentities/voicecode-worker-identity \
  --registry-server voicecodebuildsprod.azurecr.io \
  --registry-identity /subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourcegroups/voicecode/providers/Microsoft.ManagedIdentity/userAssignedIdentities/voicecode-worker-identity

# Add the claude-agent container
az containerapp update \
  --name voicecode-worker \
  --resource-group voicecode \
  --container-name claude-agent \
  --image voicecodebuildsprod.azurecr.io/claude-agent:latest \
  --cpu 0.5 \
  --memory 1

# Configure secrets from Key Vault
az containerapp secret set \
  --name voicecode-worker \
  --resource-group voicecode \
  --secrets \
    claude-api-key=keyvaultref:https://voicecodedevkveus.vault.azure.net/secrets/ClaudeApiKey,identityref:/subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourcegroups/voicecode/providers/Microsoft.ManagedIdentity/userAssignedIdentities/voicecode-worker-identity \
    servicebus-connection-string=keyvaultref:https://voicecodedevkveus.vault.azure.net/secrets/ServiceBusConnectionString,identityref:/subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourcegroups/voicecode/providers/Microsoft.ManagedIdentity/userAssignedIdentities/voicecode-worker-identity

# Set environment variables
az containerapp update \
  --name voicecode-worker \
  --resource-group voicecode \
  --set-env-vars \
    Worker__SidecarUrl=http://localhost:3000 \
    Worker__ClaudeApiKey=secretref:claude-api-key \
    ServiceBus__ConnectionString=secretref:servicebus-connection-string \
    ApplicationInsights__ConnectionString="" \
    AZURE_CLIENT_ID=df5cce2f-93b2-472e-9040-ecfca9d2f8d5 \
    KeyVaultName=voicecodedevkveus \
    ANTHROPIC_API_KEY=secretref:claude-api-key \
    WORKSPACE_ROOT=/project \
    PORT=3000