#!/bin/bash

# Deploy Worker Service with Claude Agent to Azure Container Apps
# This creates a multi-container app with file system access

set -e

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

# Configuration
RESOURCE_GROUP="voicecode-rg"
REGISTRY_NAME="voicecodebuildsprod"
ENV_NAME="voicecode-env"
WORKER_NAME="voicecode-worker-agent"
WORKER_ID="worker-agent-1"

echo -e "${BLUE}Worker Service with Claude Agent Deployment${NC}"
echo -e "${BLUE}=========================================${NC}"

# Check if logged in to Azure
echo -e "\n${YELLOW}Checking Azure login...${NC}"
az account show > /dev/null 2>&1 || az login

# Get secrets from Key Vault
echo -e "\n${YELLOW}Retrieving secrets from Key Vault...${NC}"
ANTHROPIC_KEY=$(az keyvault secret show \
    --vault-name voicecodevault-0724 \
    --name AnthropicApiKey \
    --query value -o tsv 2>/dev/null || echo "")

SERVICE_BUS_CONNECTION=$(az keyvault secret show \
    --vault-name voicecodevault-0724 \
    --name ServiceBusConnectionString \
    --query value -o tsv 2>/dev/null || echo "")

GITHUB_TOKEN=$(az keyvault secret show \
    --vault-name voicecodevault-0724 \
    --name GitHubToken \
    --query value -o tsv 2>/dev/null || echo "")

# Create Container App with multiple containers and shared volume
echo -e "\n${YELLOW}Creating multi-container app with shared volume...${NC}"

# Create YAML configuration
cat > worker-agent-config.yaml << EOF
location: East US
name: $WORKER_NAME
resourceGroup: $RESOURCE_GROUP
type: Microsoft.App/containerApps
identity:
  type: UserAssigned
  userAssignedIdentities:
    /subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourcegroups/voicecode-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/voicecode-identity: {}
properties:
  managedEnvironmentId: /subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.App/managedEnvironments/$ENV_NAME
  configuration:
    activeRevisionsMode: Single
    ingress:
      external: true
      targetPort: 80
      transport: Http
      allowInsecure: false
    registries:
      - server: $REGISTRY_NAME.azurecr.io
        identity: /subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourcegroups/voicecode-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/voicecode-identity
    secrets:
      - name: anthropic-key
        value: "$ANTHROPIC_KEY"
      - name: servicebus-connection
        value: "$SERVICE_BUS_CONNECTION"
      - name: github-token
        value: "$GITHUB_TOKEN"
  template:
    volumes:
      - name: shared-workspace
        storageType: EmptyDir
    containers:
      - name: worker
        image: $REGISTRY_NAME.azurecr.io/voicecode-worker:latest
        resources:
          cpu: 1.0
          memory: 2Gi
        volumeMounts:
          - volumeName: shared-workspace
            mountPath: /workspaces
        env:
          - name: ASPNETCORE_ENVIRONMENT
            value: Production
          - name: ASPNETCORE_URLS
            value: http://+:80
          - name: WORKER_ID
            value: $WORKER_ID
          - name: ANTHROPIC_API_KEY
            secretRef: anthropic-key
          - name: GITHUB_TOKEN
            secretRef: github-token
          - name: ServiceBus__ConnectionString
            secretRef: servicebus-connection
          - name: Worker__UseSidecar
            value: "true"
          - name: Worker__SidecarUrl
            value: http://localhost:3000
          - name: Worker__StreamQueueName
            value: sdk-stream-events
          - name: Worker__EnableStreaming
            value: "true"
          - name: Worker__WorkspaceBasePath
            value: /workspaces
      - name: agent
        image: $REGISTRY_NAME.azurecr.io/claude-agent:latest
        resources:
          cpu: 0.5
          memory: 1Gi
        volumeMounts:
          - volumeName: shared-workspace
            mountPath: /workspaces
        env:
          - name: NODE_ENV
            value: production
          - name: PORT
            value: "3000"
          - name: ANTHROPIC_API_KEY
            secretRef: anthropic-key
          - name: AZURE_SERVICE_BUS_CONNECTION_STRING
            secretRef: servicebus-connection
          - name: SDK_STREAM_QUEUE_NAME
            value: sdk-stream-events
          - name: LOG_LEVEL
            value: info
          - name: WORKER_ID
            value: $WORKER_ID
          - name: CLAUDE_MODEL
            value: claude-3-opus-20240229
          - name: CLAUDE_MAX_TOKENS
            value: "4096"
          - name: WORKSPACE_ROOT
            value: /workspaces
    scale:
      minReplicas: 1
      maxReplicas: 1
EOF

# Deploy the container app
az containerapp create \
    --name $WORKER_NAME \
    --resource-group $RESOURCE_GROUP \
    --yaml worker-agent-config.yaml

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Worker with Claude agent created successfully${NC}"
else
    echo -e "${RED}✗ Failed to create worker with agent${NC}"
    exit 1
fi

# Get the FQDN
WORKER_URL=$(az containerapp show \
    --name $WORKER_NAME \
    --resource-group $RESOURCE_GROUP \
    --query properties.configuration.ingress.fqdn -o tsv)

# Clean up
rm -f worker-agent-config.yaml

echo -e "\n${GREEN}========================================${NC}"
echo -e "${GREEN}Worker + Agent Deployment Complete${NC}"
echo -e "${GREEN}========================================${NC}"
echo -e "Worker URL: ${BLUE}https://$WORKER_URL${NC}"
echo -e "\nKey features:"
echo -e "- Worker Service clones repos to /workspaces"
echo -e "- Claude Agent can access files via /project (shared volume)"
echo -e "- Full file system tools available"
echo -e "- Real-time event streaming to Service Bus"
echo -e "\nTo test:"
echo -e "1. Check worker health: ${YELLOW}curl https://$WORKER_URL/health${NC}"
echo -e "2. Monitor logs: ${YELLOW}az containerapp logs tail -n $WORKER_NAME -g $RESOURCE_GROUP --follow${NC}"