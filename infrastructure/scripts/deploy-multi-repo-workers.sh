#!/bin/bash

# Deploy VoiceCode with Multiple Repository-Aware Workers
# This script deploys 10 workers, each assigned to a specific client repository

set -e

# Configuration
RESOURCE_GROUP="${RESOURCE_GROUP:-VoiceCode-RG}"
LOCATION="${LOCATION:-eastus}"
DEPLOYMENT_NAME="voicecode-multi-repo-$(date +%Y%m%d%H%M%S)"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}VoiceCode Multi-Repository Worker Deployment${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""

# Check prerequisites
echo -e "${YELLOW}Checking prerequisites...${NC}"

if ! command -v az &> /dev/null; then
    echo -e "${RED}Error: Azure CLI is not installed${NC}"
    exit 1
fi

# Check if logged in to Azure
if ! az account show &> /dev/null; then
    echo -e "${RED}Error: Not logged in to Azure. Run 'az login' first${NC}"
    exit 1
fi

# Get deployment parameters
echo -e "${YELLOW}Fetching deployment parameters...${NC}"

# Get Container Apps Environment ID
ENV_ID=$(az containerapp env show \
    --name voicecode-env \
    --resource-group $RESOURCE_GROUP \
    --query id -o tsv 2>/dev/null)

if [ -z "$ENV_ID" ]; then
    echo -e "${RED}Error: Container Apps Environment not found${NC}"
    exit 1
fi

# Get Container Registry
REGISTRY=$(az acr list \
    --resource-group $RESOURCE_GROUP \
    --query "[0].loginServer" -o tsv)

if [ -z "$REGISTRY" ]; then
    echo -e "${RED}Error: Container Registry not found${NC}"
    exit 1
fi

# Get Managed Identity
IDENTITY_ID=$(az identity show \
    --name voicecode-identity \
    --resource-group $RESOURCE_GROUP \
    --query id -o tsv)

IDENTITY_CLIENT_ID=$(az identity show \
    --name voicecode-identity \
    --resource-group $RESOURCE_GROUP \
    --query clientId -o tsv)

# Get Key Vault name
KEY_VAULT=$(az keyvault list \
    --resource-group $RESOURCE_GROUP \
    --query "[?starts_with(name, 'voicecodevault')].name" -o tsv | head -1)

# Get Service Bus namespace
SERVICE_BUS=$(az servicebus namespace list \
    --resource-group $RESOURCE_GROUP \
    --query "[0].name" -o tsv)

# Get Application Insights connection string
APP_INSIGHTS_CONNECTION=$(az monitor app-insights component show \
    --app voicecode-insights \
    --resource-group $RESOURCE_GROUP \
    --query connectionString -o tsv)

echo -e "${GREEN}Found deployment resources:${NC}"
echo "  - Environment: voicecode-env"
echo "  - Registry: $REGISTRY"
echo "  - Key Vault: $KEY_VAULT"
echo "  - Service Bus: $SERVICE_BUS"
echo ""

# Build and push worker image if requested
if [ "$BUILD_IMAGE" == "true" ]; then
    echo -e "${YELLOW}Building and pushing worker image...${NC}"
    
    # Navigate to repository root
    cd "$(dirname "$0")/../.."
    
    # Build worker image
    az acr build \
        --registry ${REGISTRY%%.azurecr.io} \
        --image voicecode/worker-service:latest \
        --file services/worker-service/Dockerfile \
        .
    
    echo -e "${GREEN}Worker image built and pushed successfully${NC}"
fi

# Create Service Bus queues for each worker
echo -e "${YELLOW}Creating Service Bus queues for workers...${NC}"

for i in {1..10}; do
    QUEUE_NAME="worker-$i-tasks"
    
    # Check if queue exists
    if ! az servicebus queue show \
        --name $QUEUE_NAME \
        --namespace-name $SERVICE_BUS \
        --resource-group $RESOURCE_GROUP &>/dev/null; then
        
        echo "Creating queue: $QUEUE_NAME"
        az servicebus queue create \
            --name $QUEUE_NAME \
            --namespace-name $SERVICE_BUS \
            --resource-group $RESOURCE_GROUP \
            --max-size 1024 \
            --default-message-time-to-live P7D \
            --duplicate-detection-history-time-window PT10M \
            --enable-duplicate-detection true \
            --output none
    else
        echo "Queue already exists: $QUEUE_NAME"
    fi
done

echo -e "${GREEN}Service Bus queues ready${NC}"
echo ""

# Copy repo registry to orchestrator
echo -e "${YELLOW}Copying repository registry to orchestrator...${NC}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_REGISTRY="$SCRIPT_DIR/../container-apps/repo-registry.yaml"

if [ -f "$REPO_REGISTRY" ]; then
    cp "$REPO_REGISTRY" "$SCRIPT_DIR/../../services/orchestrator-service/repo-registry.yaml"
    echo -e "${GREEN}Repository registry copied${NC}"
else
    echo -e "${YELLOW}Warning: repo-registry.yaml not found${NC}"
fi

# Deploy workers using Bicep template
echo -e "${YELLOW}Deploying 10 repository-specific workers...${NC}"

az deployment group create \
    --name $DEPLOYMENT_NAME \
    --resource-group $RESOURCE_GROUP \
    --template-file infrastructure/container-apps/worker-apps-multi-repo.bicep \
    --parameters \
        environmentId=$ENV_ID \
        containerRegistryLoginServer=$REGISTRY \
        managedIdentityId=$IDENTITY_ID \
        managedIdentityClientId=$IDENTITY_CLIENT_ID \
        keyVaultName=$KEY_VAULT \
        serviceBusNamespace=$SERVICE_BUS \
        appInsightsConnectionString="$APP_INSIGHTS_CONNECTION" \
        imageTag="${IMAGE_TAG:-latest}"

if [ $? -eq 0 ]; then
    echo -e "${GREEN}Worker deployment completed successfully!${NC}"
else
    echo -e "${RED}Worker deployment failed${NC}"
    exit 1
fi

# Display deployment summary
echo ""
echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}Deployment Summary${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""

echo -e "${GREEN}Workers deployed:${NC}"
echo "  1. Restaurant POS System (worker-1)"
echo "  2. Law Firm Case Management (worker-2)"
echo "  3. Startup SaaS Platform (worker-3)"
echo "  4. Bank Mobile App (worker-4)"
echo "  5. E-commerce Store (worker-5)"
echo "  6. Healthcare Patient Portal (worker-6)"
echo "  7. Real Estate Listings (worker-7)"
echo "  8. School Parent Portal (worker-8)"
echo "  9. Fitness Tracker App (worker-9)"
echo "  10. Nonprofit Donations (worker-10)"
echo ""

echo -e "${GREEN}Key features:${NC}"
echo "  - Each worker clones its assigned repository on startup"
echo "  - Workers scale to 0 when idle (cost-effective)"
echo "  - Voice commands route to appropriate worker based on keywords"
echo "  - Claude Code handles all task planning and execution"
echo ""

echo -e "${YELLOW}Next steps:${NC}"
echo "  1. Ensure GitHub tokens are set in Key Vault"
echo "  2. Update orchestrator service with routing logic"
echo "  3. Test voice commands for each client domain"
echo ""

# Show worker status
echo -e "${YELLOW}Checking worker status...${NC}"
az containerapp list \
    --resource-group $RESOURCE_GROUP \
    --query "[?starts_with(name, 'voicecode-worker-')].{Name:name, Status:properties.provisioningState, Replicas:properties.template.scale.minReplicas}" \
    --output table

echo ""
echo -e "${GREEN}Deployment complete!${NC}"