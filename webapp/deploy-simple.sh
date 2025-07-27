#!/bin/bash

# Simple deployment script for manual webapp deployment
echo "🚀 Deploying webapp version 1.0.9..."

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
echo "Version: 1.0.9"
echo "Build time: $(date)"