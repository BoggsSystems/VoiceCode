#!/bin/bash

# Deploy VoiceCode Web App to Azure Static Web Apps

set -e

echo "🚀 Starting VoiceCode Web App deployment..."

# Configuration
RESOURCE_GROUP="voicecode-rg"
STATIC_WEB_APP_NAME="voicecode-webapp"
LOCATION="eastus2"
SKU="Free"  # Can be: Free, Standard

# Change to webapp directory
cd /Users/jeffboggs/VoiceCode/webapp

# Build the React app if not already built
if [ ! -d "build" ]; then
    echo "📦 Building React app..."
    npm run build
else
    echo "✅ Using existing build"
fi

# Check if Static Web App exists
echo "🔍 Checking if Static Web App exists..."
if ! az staticwebapp show --name $STATIC_WEB_APP_NAME --resource-group $RESOURCE_GROUP &>/dev/null; then
    echo "📱 Creating Static Web App..."
    # Create without GitHub integration
    az staticwebapp create \
        --name $STATIC_WEB_APP_NAME \
        --resource-group $RESOURCE_GROUP \
        --location $LOCATION \
        --sku $SKU
else
    echo "✅ Static Web App already exists"
fi

# Get deployment token
echo "🔑 Getting deployment token..."
DEPLOYMENT_TOKEN=$(az staticwebapp secrets list \
    --name $STATIC_WEB_APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "properties.apiKey" -o tsv)

# Deploy using SWA CLI with npx
echo "🚀 Deploying to Azure Static Web Apps..."
npx @azure/static-web-apps-cli deploy ./build \
    --deployment-token $DEPLOYMENT_TOKEN \
    --env production

# Get the URL
APP_URL=$(az staticwebapp show \
    --name $STATIC_WEB_APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "defaultHostname" -o tsv)

echo "✅ Deployment complete!"
echo "🌐 Your app is available at: https://$APP_URL"
echo ""
echo "📱 You can now access the voice interface from your phone at:"
echo "   https://$APP_URL/voice"