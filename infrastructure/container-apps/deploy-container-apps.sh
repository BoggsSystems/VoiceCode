#!/bin/bash

# Deploy VoiceCode to Azure Container Apps
# Phase 4: Container Apps Migration with KEDA autoscaling

set -e

# Configuration
RESOURCE_GROUP="${RESOURCE_GROUP:-voicecode-rg}"
LOCATION="${LOCATION:-eastus}"
ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-voicecode}"
IMAGE_TAG="${IMAGE_TAG:-latest}"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')]${NC} $1"
}

print_error() {
    echo -e "${RED}[$(date +'%Y-%m-%d %H:%M:%S')] ERROR:${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[$(date +'%Y-%m-%d %H:%M:%S')] WARNING:${NC} $1"
}

echo "========================================="
echo "VoiceCode Container Apps Deployment"
echo "========================================="
echo "Resource Group: $RESOURCE_GROUP"
echo "Location: $LOCATION"
echo "Environment: $ENVIRONMENT_NAME"
echo "Image Tag: $IMAGE_TAG"
echo "========================================="

# Check if logged in to Azure
print_status "Checking Azure login status..."
if ! az account show &>/dev/null; then
    print_error "Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

# Check if resource group exists
print_status "Checking resource group..."
if ! az group show --name $RESOURCE_GROUP &>/dev/null; then
    print_error "Resource group $RESOURCE_GROUP not found"
    exit 1
fi

# Deploy infrastructure
print_status "Deploying Container Apps infrastructure..."
DEPLOYMENT_OUTPUT=$(az deployment group create \
    --resource-group $RESOURCE_GROUP \
    --template-file main.bicep \
    --parameters location=$LOCATION \
                 environmentName=$ENVIRONMENT_NAME \
    --query "properties.outputs" \
    -o json)

if [ $? -ne 0 ]; then
    print_error "Infrastructure deployment failed"
    exit 1
fi

# Extract outputs
ENVIRONMENT_ID=$(echo $DEPLOYMENT_OUTPUT | jq -r '.environmentId.value')
MANAGED_IDENTITY_ID=$(echo $DEPLOYMENT_OUTPUT | jq -r '.managedIdentityId.value')
MANAGED_IDENTITY_CLIENT_ID=$(echo $DEPLOYMENT_OUTPUT | jq -r '.managedIdentityClientId.value')
CONTAINER_REGISTRY_LOGIN_SERVER=$(echo $DEPLOYMENT_OUTPUT | jq -r '.containerRegistryLoginServer.value')

print_status "Container Apps Environment created: $ENVIRONMENT_ID"

# Get required values from Key Vault
print_status "Retrieving configuration from Key Vault..."
KEY_VAULT_NAME="voicecodedevkveus"
SERVICE_BUS_NAMESPACE="voicecodeservicebus"
STORAGE_ACCOUNT_NAME="voicecodestorage"
APP_INSIGHTS_NAME="voicecode-insights"

# Get Application Insights connection string
APP_INSIGHTS_CONN=$(az monitor app-insights component show \
    --app $APP_INSIGHTS_NAME \
    --resource-group $RESOURCE_GROUP \
    --query connectionString \
    -o tsv)

# Build and push images if needed
if [ "$BUILD_IMAGES" = "true" ]; then
    print_status "Building and pushing Docker images..."
    
    # Build orchestrator
    print_status "Building orchestrator service..."
    docker build -f ../../services/orchestrator-service/Dockerfile \
        -t ${CONTAINER_REGISTRY_LOGIN_SERVER}/voicecode/orchestrator-service:${IMAGE_TAG} \
        ../..
    
    # Build worker
    print_status "Building worker service..."
    docker build -f ../../services/worker-service/Dockerfile \
        -t ${CONTAINER_REGISTRY_LOGIN_SERVER}/voicecode/worker-service:${IMAGE_TAG} \
        ../..
    
    # Push images
    print_status "Pushing images to ACR..."
    az acr login --name ${CONTAINER_REGISTRY_LOGIN_SERVER%%.*}
    docker push ${CONTAINER_REGISTRY_LOGIN_SERVER}/voicecode/orchestrator-service:${IMAGE_TAG}
    docker push ${CONTAINER_REGISTRY_LOGIN_SERVER}/voicecode/worker-service:${IMAGE_TAG}
fi

# Deploy Orchestrator
print_status "Deploying Orchestrator Container App..."
az deployment group create \
    --resource-group $RESOURCE_GROUP \
    --template-file orchestrator-app.bicep \
    --parameters environmentId=$ENVIRONMENT_ID \
                 containerRegistryLoginServer=$CONTAINER_REGISTRY_LOGIN_SERVER \
                 managedIdentityId=$MANAGED_IDENTITY_ID \
                 managedIdentityClientId=$MANAGED_IDENTITY_CLIENT_ID \
                 keyVaultName=$KEY_VAULT_NAME \
                 serviceBusNamespace=$SERVICE_BUS_NAMESPACE \
                 appInsightsConnectionString="$APP_INSIGHTS_CONN" \
                 imageTag=$IMAGE_TAG

if [ $? -ne 0 ]; then
    print_error "Orchestrator deployment failed"
    exit 1
fi

# Deploy Worker with auto-scaling
print_status "Deploying Worker Container App with KEDA scaling..."
az deployment group create \
    --resource-group $RESOURCE_GROUP \
    --template-file worker-app.bicep \
    --parameters environmentId=$ENVIRONMENT_ID \
                 containerRegistryLoginServer=$CONTAINER_REGISTRY_LOGIN_SERVER \
                 managedIdentityId=$MANAGED_IDENTITY_ID \
                 managedIdentityClientId=$MANAGED_IDENTITY_CLIENT_ID \
                 keyVaultName=$KEY_VAULT_NAME \
                 serviceBusNamespace=$SERVICE_BUS_NAMESPACE \
                 storageAccountName=$STORAGE_ACCOUNT_NAME \
                 appInsightsConnectionString="$APP_INSIGHTS_CONN" \
                 imageTag=$IMAGE_TAG \
                 minReplicas=0 \
                 maxReplicas=10

if [ $? -ne 0 ]; then
    print_error "Worker deployment failed"
    exit 1
fi

# Deploy other services (if needed)
OTHER_SERVICES=("stt" "tts" "claude" "generator" "router" "dispatcher")

for service in "${OTHER_SERVICES[@]}"; do
    if [ -f "${service}-app.bicep" ]; then
        print_status "Deploying $service service..."
        az deployment group create \
            --resource-group $RESOURCE_GROUP \
            --template-file ${service}-app.bicep \
            --parameters environmentId=$ENVIRONMENT_ID \
                         containerRegistryLoginServer=$CONTAINER_REGISTRY_LOGIN_SERVER \
                         managedIdentityId=$MANAGED_IDENTITY_ID \
                         managedIdentityClientId=$MANAGED_IDENTITY_CLIENT_ID \
                         keyVaultName=$KEY_VAULT_NAME \
                         appInsightsConnectionString="$APP_INSIGHTS_CONN" \
                         imageTag=$IMAGE_TAG
    fi
done

# Get deployment information
print_status "Getting deployment information..."

ORCHESTRATOR_URL=$(az containerapp show \
    --name voicecode-orchestrator \
    --resource-group $RESOURCE_GROUP \
    --query "properties.configuration.ingress.fqdn" \
    -o tsv)

WORKER_COUNT=$(az containerapp replica list \
    --name voicecode-worker \
    --resource-group $RESOURCE_GROUP \
    --query "length(@)" \
    -o tsv)

echo ""
echo "========================================="
echo "Deployment Complete!"
echo "========================================="
print_status "Orchestrator URL: https://$ORCHESTRATOR_URL"
print_status "Worker replicas: $WORKER_COUNT"
echo ""
echo "Useful commands:"
echo "  View orchestrator logs:"
echo "    az containerapp logs show -n voicecode-orchestrator -g $RESOURCE_GROUP"
echo ""
echo "  View worker logs:"
echo "    az containerapp logs show -n voicecode-worker -g $RESOURCE_GROUP"
echo ""
echo "  Scale workers manually:"
echo "    az containerapp update -n voicecode-worker -g $RESOURCE_GROUP --min-replicas 1 --max-replicas 20"
echo ""
echo "  View scaling metrics:"
echo "    az containerapp revision list -n voicecode-worker -g $RESOURCE_GROUP -o table"
echo ""
echo "Test the deployment:"
echo "  curl https://$ORCHESTRATOR_URL/health"
echo "  curl https://$ORCHESTRATOR_URL/api/orchestration/pool/status"