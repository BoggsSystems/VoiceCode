#!/bin/bash

# VoiceCode Container Instances Deployment with Managed Identities
# Comprehensive deployment script with managed identities and full configuration

set -e

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
KEY_VAULT_NAME="voicecodedevkveus"
ACR_NAME="voicecodebuildsprod"
ACR_SERVER="${ACR_NAME}.azurecr.io"
ENVIRONMENT="Production"
PROJECT_PREFIX="voicecode-dev-eus"

# API Keys
CLAUDE_API_KEY="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"
OPENAI_API_KEY="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"

# Function to create/get managed identity
ensure_managed_identity() {
    local SERVICE_NAME=$1
    local IDENTITY_NAME="${PROJECT_PREFIX}-id-${SERVICE_NAME}"
    
    echo "Ensuring managed identity for $SERVICE_NAME..."
    
    # Check if identity exists
    IDENTITY_ID=$(az identity show --name $IDENTITY_NAME --resource-group $RESOURCE_GROUP --query id -o tsv 2>/dev/null || echo "")
    
    if [ -z "$IDENTITY_ID" ]; then
        echo "Creating managed identity: $IDENTITY_NAME"
        az identity create \
            --name $IDENTITY_NAME \
            --resource-group $RESOURCE_GROUP \
            --location $LOCATION \
            --output none
        
        IDENTITY_ID=$(az identity show --name $IDENTITY_NAME --resource-group $RESOURCE_GROUP --query id -o tsv)
    fi
    
    # Get identity details
    IDENTITY_CLIENT_ID=$(az identity show --name $IDENTITY_NAME --resource-group $RESOURCE_GROUP --query clientId -o tsv)
    IDENTITY_PRINCIPAL_ID=$(az identity show --name $IDENTITY_NAME --resource-group $RESOURCE_GROUP --query principalId -o tsv)
    
    echo "Identity ID: $IDENTITY_ID"
    echo "Client ID: $IDENTITY_CLIENT_ID"
    
    # Grant Key Vault access
    echo "Granting Key Vault access..."
    az keyvault set-policy \
        --name $KEY_VAULT_NAME \
        --object-id $IDENTITY_PRINCIPAL_ID \
        --secret-permissions get list \
        --output none 2>/dev/null || true
    
    # Grant ACR pull access
    echo "Granting ACR pull access..."
    ACR_ID=$(az acr show --name $ACR_NAME --query id -o tsv)
    az role assignment create \
        --assignee $IDENTITY_PRINCIPAL_ID \
        --role "AcrPull" \
        --scope $ACR_ID \
        --output none 2>/dev/null || true
    
    echo "✓ Managed identity configured for $SERVICE_NAME"
    
    # Return the identity ID
    echo $IDENTITY_ID
}

# Function to deploy a service with managed identity
deploy_service_with_identity() {
    local SERVICE_NAME=$1
    local CPU=$2
    local MEMORY=$3
    local IMAGE_TAG=$4
    
    echo "========================================="
    echo "Deploying $SERVICE_NAME service..."
    echo "========================================="
    
    # Ensure managed identity exists
    IDENTITY_ID=$(ensure_managed_identity $SERVICE_NAME)
    
    # Get ACR credentials
    ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
    ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)
    
    # Get Key Vault secrets
    echo "Getting Key Vault secrets..."
    APP_INSIGHTS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name ApplicationInsightsConnectionString --query value -o tsv 2>/dev/null || echo "")
    SERVICE_BUS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name ServiceBusConnectionString --query value -o tsv 2>/dev/null || echo "")
    REDIS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name RedisConnectionString --query value -o tsv 2>/dev/null || echo "")
    STORAGE_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name StorageConnectionString --query value -o tsv 2>/dev/null || echo "")
    COSMOS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name CosmosDbConnectionString --query value -o tsv 2>/dev/null || echo "")
    AZURE_SPEECH_KEY=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name AzureSpeechKey --query value -o tsv 2>/dev/null || echo "")
    
    # Get tenant ID
    TENANT_ID=$(az account show --query tenantId -o tsv)
    
    # Container name
    CONTAINER_NAME="${PROJECT_PREFIX}-${SERVICE_NAME}-ci"
    
    # Create YAML configuration file
    cat > "${SERVICE_NAME}-container.yaml" << EOF
apiVersion: 2019-12-01
location: $LOCATION
name: $CONTAINER_NAME
properties:
  containers:
  - name: $SERVICE_NAME
    properties:
      image: ${ACR_SERVER}/voicecode/${SERVICE_NAME}-service:${IMAGE_TAG}
      resources:
        requests:
          cpu: $CPU
          memoryInGb: $MEMORY
      ports:
      - port: 80
        protocol: TCP
      - port: 443
        protocol: TCP
      environmentVariables:
      - name: ASPNETCORE_ENVIRONMENT
        value: $ENVIRONMENT
      - name: ASPNETCORE_URLS
        value: http://+:80
      - name: SERVICE_NAME
        value: $SERVICE_NAME
      - name: CONTAINER_MODE
        value: 'true'
      - name: KeyVaultName
        value: $KEY_VAULT_NAME
      - name: AzureAd__TenantId
        value: $TENANT_ID
      - name: AzureAd__Instance
        value: https://login.microsoftonline.com/
      - name: Cors__AllowedOrigins__0
        value: https://voicecode.dev
      - name: Cors__AllowedOrigins__1
        value: http://localhost:3000
EOF

    # Add service-specific environment variables
    case $SERVICE_NAME in
        "claude")
            cat >> "${SERVICE_NAME}-container.yaml" << EOF
      - name: ANTHROPIC_API_KEY
        secureValue: $CLAUDE_API_KEY
EOF
            ;;
        "dispatcher")
            cat >> "${SERVICE_NAME}-container.yaml" << EOF
      - name: ServiceEndpoints__STTService
        value: https://${PROJECT_PREFIX}-stt.${LOCATION}.azurecontainer.io
      - name: ServiceEndpoints__ClaudeService
        value: https://${PROJECT_PREFIX}-claude.${LOCATION}.azurecontainer.io
      - name: ServiceEndpoints__RouterService
        value: https://${PROJECT_PREFIX}-router.${LOCATION}.azurecontainer.io
      - name: ServiceEndpoints__GeneratorService
        value: https://${PROJECT_PREFIX}-generator.${LOCATION}.azurecontainer.io
      - name: ServiceEndpoints__TTSService
        value: https://${PROJECT_PREFIX}-tts.${LOCATION}.azurecontainer.io
EOF
            ;;
        "generator")
            cat >> "${SERVICE_NAME}-container.yaml" << EOF
      - name: OPENAI_API_KEY
        secureValue: $OPENAI_API_KEY
EOF
            ;;
        "stt" | "tts")
            cat >> "${SERVICE_NAME}-container.yaml" << EOF
      - name: AzureSpeech__Key
        secureValue: $AZURE_SPEECH_KEY
      - name: AzureSpeech__Region
        value: $LOCATION
EOF
            ;;
    esac

    # Add secure environment variables
    cat >> "${SERVICE_NAME}-container.yaml" << EOF
      - name: ApplicationInsights__ConnectionString
        secureValue: $APP_INSIGHTS_CONN
      - name: ConnectionStrings__ServiceBus
        secureValue: $SERVICE_BUS_CONN
      - name: ConnectionStrings__Redis
        secureValue: $REDIS_CONN
      - name: ConnectionStrings__Storage
        secureValue: $STORAGE_CONN
      - name: ConnectionStrings__CosmosDb
        secureValue: $COSMOS_CONN
      livenessProbe:
        httpGet:
          path: /health
          port: 80
          scheme: Http
        initialDelaySeconds: 30
        periodSeconds: 30
        timeoutSeconds: 5
        failureThreshold: 3
      readinessProbe:
        httpGet:
          path: /health/ready
          port: 80
          scheme: Http
        initialDelaySeconds: 15
        periodSeconds: 10
        timeoutSeconds: 3
        failureThreshold: 3
  osType: Linux
  restartPolicy: Always
  ipAddress:
    type: Public
    dnsNameLabel: ${PROJECT_PREFIX}-${SERVICE_NAME}
    ports:
    - protocol: TCP
      port: 80
    - protocol: TCP
      port: 443
  imageRegistryCredentials:
  - server: $ACR_SERVER
    username: $ACR_USERNAME
    password: $ACR_PASSWORD
  identity:
    type: UserAssigned
    userAssignedIdentities:
      $IDENTITY_ID: {}
tags:
  Environment: $ENVIRONMENT
  Service: $SERVICE_NAME
  Type: ContainerInstance
type: Microsoft.ContainerInstance/containerGroups
EOF

    # Deploy using the YAML file
    echo "Creating container instance..."
    az container create \
        --resource-group $RESOURCE_GROUP \
        --file "${SERVICE_NAME}-container.yaml" \
        --output table
    
    # Clean up YAML file
    rm -f "${SERVICE_NAME}-container.yaml"
    
    # Wait for container to be ready
    echo "Waiting for $SERVICE_NAME to be ready..."
    sleep 20
    
    # Check deployment status
    STATUS=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query instanceView.state -o tsv)
    echo "Container status: $STATUS"
    
    # Get FQDN
    FQDN=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query ipAddress.fqdn -o tsv)
    echo "Service deployed at: https://$FQDN"
    
    # Test health endpoint
    echo "Testing health endpoint..."
    for i in {1..5}; do
        if curl -s -o /dev/null -w "Health check attempt $i: %{http_code}\n" "http://$FQDN/health"; then
            echo "✓ Health check passed"
            break
        else
            echo "Health check attempt $i failed, retrying..."
            sleep 5
        fi
    done
    
    echo "✓ $SERVICE_NAME service deployed successfully"
}

# Main deployment
echo "========================================="
echo "VoiceCode Container Instances Deployment"
echo "========================================="
echo "Resource Group: $RESOURCE_GROUP"
echo "Location: $LOCATION"
echo "Key Vault: $KEY_VAULT_NAME"
echo "ACR: $ACR_NAME"
echo "========================================="

# Check prerequisites
echo "Checking prerequisites..."

# Check if resource group exists
if ! az group show --name $RESOURCE_GROUP &>/dev/null; then
    echo "ERROR: Resource group $RESOURCE_GROUP not found"
    exit 1
fi

# Check if Key Vault exists
if ! az keyvault show --name $KEY_VAULT_NAME &>/dev/null; then
    echo "ERROR: Key Vault $KEY_VAULT_NAME not found"
    exit 1
fi

# Check if ACR exists
if ! az acr show --name $ACR_NAME &>/dev/null; then
    echo "ERROR: ACR $ACR_NAME not found"
    exit 1
fi

echo "✓ All prerequisites met"

# Get latest image tag
echo "Getting latest image tags from ACR..."
LATEST_TAG=$(az acr repository show-tags --name $ACR_NAME --repository voicecode/claude-service --orderby time_desc --top 1 -o tsv 2>/dev/null || echo "v3")
echo "Using image tag: $LATEST_TAG"

# Deploy services in the specified order
deploy_service_with_identity "claude" "1.0" "2.0" "$LATEST_TAG"
deploy_service_with_identity "stt" "0.5" "1.0" "$LATEST_TAG"
deploy_service_with_identity "tts" "0.5" "1.0" "$LATEST_TAG"
deploy_service_with_identity "dispatcher" "0.5" "1.0" "$LATEST_TAG"
deploy_service_with_identity "generator" "1.0" "2.0" "$LATEST_TAG"
deploy_service_with_identity "router" "0.5" "1.0" "$LATEST_TAG"

echo "========================================="
echo "All services deployed successfully!"
echo "========================================="

# List all deployed container instances
echo ""
echo "Deployed container instances:"
az container list --resource-group $RESOURCE_GROUP --output table

# Show service URLs
echo ""
echo "Service URLs:"
echo "Claude: https://${PROJECT_PREFIX}-claude.${LOCATION}.azurecontainer.io"
echo "STT: https://${PROJECT_PREFIX}-stt.${LOCATION}.azurecontainer.io"
echo "TTS: https://${PROJECT_PREFIX}-tts.${LOCATION}.azurecontainer.io"
echo "Dispatcher: https://${PROJECT_PREFIX}-dispatcher.${LOCATION}.azurecontainer.io"
echo "Generator: https://${PROJECT_PREFIX}-generator.${LOCATION}.azurecontainer.io"
echo "Router: https://${PROJECT_PREFIX}-router.${LOCATION}.azurecontainer.io"

# Show logs command
echo ""
echo "To view logs for a service, use:"
echo "az container logs --resource-group $RESOURCE_GROUP --name ${PROJECT_PREFIX}-<service>-ci"

# Show health check command
echo ""
echo "To check health status for a service, use:"
echo "curl http://${PROJECT_PREFIX}-<service>.${LOCATION}.azurecontainer.io/health"