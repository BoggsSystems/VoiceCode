#!/bin/bash

# Simple deployment script for manual webapp deployment
echo "🚀 Deploying webapp version 1.0.17..."

# Check if swa CLI is installed
if ! command -v swa &> /dev/null; then
    echo "⚠️  Azure Static Web Apps CLI not found. Installing..."
    npm install -g @azure/static-web-apps-cli
fi

# Deploy using SWA CLI
echo "📦 Deploying to Azure Static Web Apps..."
swa deploy ./build --deployment-token "$(az staticwebapp secrets list --name gentle-river-0d0d70a0f --resource-group voicecode-rg --query 'properties.apiKey' -o tsv 2>/dev/null)" 2>/dev/null

if [ $? -ne 0 ]; then
    echo "⚠️  SWA CLI deployment failed. Preparing files for manual deployment..."
    
    # Copy build files to a temporary directory for manual upload
    DEPLOY_DIR="/tmp/voicecode-webapp-deploy"
    rm -rf $DEPLOY_DIR
    mkdir -p $DEPLOY_DIR
    
    cp -r build/* $DEPLOY_DIR/
    
    echo "✅ Build files prepared in: $DEPLOY_DIR"
    echo ""
    echo "📋 Next steps for manual deployment:"
    echo "1. Navigate to Azure Portal > Static Web Apps > gentle-river-0d0d70a0f"
    echo "2. Click on 'Deployment history'"
    echo "3. Use the manual upload option or GitHub Actions"
    echo ""
    echo "Version: 1.0.17"
    echo "Build time: $(date)"
else
    echo "✅ Deployment successful!"
    echo "Version: 1.0.17"
    echo "Build time: $(date)"
    echo "URL: https://gentle-river-0d0d70a0f.2.azurestaticapps.net"
fi