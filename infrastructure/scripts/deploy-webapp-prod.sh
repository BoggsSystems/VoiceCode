#!/bin/bash

# Deploy VoiceCode Web App to Production Azure Static Web Apps

set -e

echo "🚀 Starting VoiceCode Web App deployment to production..."

# Configuration
RESOURCE_GROUP="voicecode-build-rg"
STATIC_WEB_APP_NAME="voicecodebuildsstatic"
DEPLOYMENT_TOKEN_VAR="AZURE_STATIC_WEB_APPS_API_TOKEN_YELLOW_COAST_05996A50F"

# Change to webapp directory
cd /Users/jeffboggs/VoiceCode/webapp

# Build the React app if not already built
if [ ! -d "build" ]; then
    echo "📦 Building React app..."
    npm run build
else
    echo "✅ Using existing build"
fi

# Get deployment token
echo "🔑 Getting deployment token..."
DEPLOYMENT_TOKEN="${!DEPLOYMENT_TOKEN_VAR}"

if [ -z "$DEPLOYMENT_TOKEN" ]; then
    echo "❌ Deployment token not found in environment variable: $DEPLOYMENT_TOKEN_VAR"
    echo "   Please set this environment variable with your deployment token"
    exit 1
fi

# Deploy using SWA CLI
echo "🚀 Deploying to Azure Static Web Apps..."
npx @azure/static-web-apps-cli deploy ./build \
    --deployment-token $DEPLOYMENT_TOKEN \
    --env production

echo ""
echo "✅ Deployment complete!"
echo "🌐 Your app is available at: https://yellow-coast-05996a50f.1.azurestaticapps.net"
echo ""
echo "📱 You can now test the voice interface with authentication at:"
echo "   https://yellow-coast-05996a50f.1.azurestaticapps.net/login-simple"