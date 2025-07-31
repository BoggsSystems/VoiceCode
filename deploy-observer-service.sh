#!/bin/bash

# Deploy Observer Service to Azure Container Apps
# This script creates the Observer Service without Bicep template

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
NAMESPACE="voicecodebus-0724"
ENV_NAME="voicecode-env"
SERVICE_NAME="voicecode-observer"

echo -e "${BLUE}Observer Service Deployment${NC}"
echo -e "${BLUE}==========================${NC}"

# Check if logged in to Azure
echo -e "\n${YELLOW}Checking Azure login...${NC}"
az account show > /dev/null 2>&1 || az login

# Create Service Bus queue
echo -e "\n${YELLOW}Creating Service Bus queue...${NC}"
az servicebus queue create \
    --resource-group $RESOURCE_GROUP \
    --namespace-name $NAMESPACE \
    --name sdk-stream-events \
    --max-size 1024 \
    --default-message-time-to-live P1D \
    --max-delivery-count 10 2>/dev/null || echo "Queue already exists"

# Get required values
echo -e "\n${YELLOW}Gathering environment information...${NC}"
SERVICE_BUS_CONNECTION=$(az servicebus namespace authorization-rule keys list \
    --resource-group $RESOURCE_GROUP \
    --namespace-name $NAMESPACE \
    --name RootManageSharedAccessKey \
    --query primaryConnectionString -o tsv)

APP_INSIGHTS_CONNECTION=$(az monitor app-insights component show \
    --app voicecodeai-dev \
    --resource-group $RESOURCE_GROUP \
    --query connectionString -o tsv 2>/dev/null || echo "")

# Create Observer Service
echo -e "\n${YELLOW}Creating Observer Service container app...${NC}"
az containerapp create \
    --name $SERVICE_NAME \
    --resource-group $RESOURCE_GROUP \
    --environment $ENV_NAME \
    --image $REGISTRY_NAME.azurecr.io/voicecode-observer:latest \
    --target-port 8080 \
    --ingress external \
    --min-replicas 1 \
    --max-replicas 3 \
    --cpu 0.5 \
    --memory 1.0Gi \
    --registry-server $REGISTRY_NAME.azurecr.io \
    --secrets \
        "servicebus-connection-string=$SERVICE_BUS_CONNECTION" \
        "openai-api-key=your-openai-key-here" \
    --env-vars \
        "ASPNETCORE_ENVIRONMENT=Production" \
        "ApplicationInsights__ConnectionString=$APP_INSIGHTS_CONNECTION" \
        "ServiceBus__ConnectionString=secretref:servicebus-connection-string" \
        "ServiceBus__StreamQueueName=sdk-stream-events" \
        "ServiceBus__NarrationQueueName=tts-requests" \
        "Observer__ServiceName=ObserverService" \
        "Observer__MaxConcurrentStreams=10" \
        "Observer__NarrationThrottleMs=2000" \
        "OpenAI__ApiKey=secretref:openai-api-key" \
        "OpenAI__Endpoint=https://YOUR-RESOURCE.openai.azure.com/" \
        "OpenAI__DeploymentName=gpt-4" \
        "AZURE_CLIENT_ID=" \
    --scale-rule-name queue-rule \
    --scale-rule-type azure-queue \
    --scale-rule-metadata \
        "queueName=sdk-stream-events" \
        "queueLength=5" \
        "connectionFromEnv=ServiceBus__ConnectionString" \
    --scale-rule-auth "connection=servicebus-connection-string"

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Observer Service created successfully${NC}"
else
    echo -e "${RED}✗ Failed to create Observer Service${NC}"
    exit 1
fi

# Get the FQDN
OBSERVER_URL=$(az containerapp show \
    --name $SERVICE_NAME \
    --resource-group $RESOURCE_GROUP \
    --query properties.configuration.ingress.fqdn -o tsv)

echo -e "\n${GREEN}========================================${NC}"
echo -e "${GREEN}Observer Service Deployment Complete${NC}"
echo -e "${GREEN}========================================${NC}"
echo -e "Service URL: ${BLUE}https://$OBSERVER_URL${NC}"
echo -e "\nNext steps:"
echo -e "1. Update OpenAI API key:"
echo -e "   ${YELLOW}az containerapp secret set -n $SERVICE_NAME -g $RESOURCE_GROUP --secrets openai-api-key=<YOUR-KEY>${NC}"
echo -e ""
echo -e "2. Update OpenAI endpoint:"
echo -e "   ${YELLOW}az containerapp update -n $SERVICE_NAME -g $RESOURCE_GROUP --set-env-vars OpenAI__Endpoint=<YOUR-ENDPOINT>${NC}"
echo -e ""
echo -e "3. Test health endpoint:"
echo -e "   ${YELLOW}curl https://$OBSERVER_URL/health${NC}"
echo -e ""
echo -e "4. Monitor logs:"
echo -e "   ${YELLOW}az containerapp logs tail -n $SERVICE_NAME -g $RESOURCE_GROUP --follow${NC}"