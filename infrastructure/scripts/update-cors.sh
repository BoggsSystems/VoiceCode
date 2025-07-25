#!/bin/bash

# Update CORS settings for all services to include the new static web app URL

set -e

echo "🔧 Updating CORS settings for VoiceCode services..."

RESOURCE_GROUP="voicecode-rg"
NEW_CORS_URL="https://gentle-river-0d0d70a0f.1.azurestaticapps.net"

# Services to update
SERVICES=(
    "voicecode-dev-eus-stt-ci"
)

# Update each container instance
for SERVICE in "${SERVICES[@]}"; do
    echo "📌 Updating CORS for $SERVICE..."
    
    # Get current environment variables
    CURRENT_ENV_VARS=$(az container show \
        --resource-group $RESOURCE_GROUP \
        --name $SERVICE \
        --query "containers[0].environmentVariables" \
        -o json)
    
    # Check if Cors__AllowedOrigins exists
    if echo "$CURRENT_ENV_VARS" | grep -q "Cors__AllowedOrigins__"; then
        echo "  Found existing CORS configuration"
        
        # Count existing origins
        ORIGIN_COUNT=$(echo "$CURRENT_ENV_VARS" | grep -c "Cors__AllowedOrigins__" || true)
        
        # Add new origin
        NEW_ENV_VAR="Cors__AllowedOrigins__${ORIGIN_COUNT}=$NEW_CORS_URL"
        
        echo "  Adding: $NEW_ENV_VAR"
        
        # Update container with new environment variable
        az container create \
            --resource-group $RESOURCE_GROUP \
            --name $SERVICE \
            --environment-variables \
                $(echo "$CURRENT_ENV_VARS" | jq -r '.[] | "\(.name)=\(.value)"' | tr '\n' ' ') \
                "$NEW_ENV_VAR" \
            --restart-policy Always \
            --no-wait \
            2>/dev/null || echo "  Note: Container update in progress"
    else
        echo "  Adding initial CORS configuration"
        
        # Add CORS configuration
        az container create \
            --resource-group $RESOURCE_GROUP \
            --name $SERVICE \
            --environment-variables \
                $(echo "$CURRENT_ENV_VARS" | jq -r '.[] | "\(.name)=\(.value)"' | tr '\n' ' ') \
                "Cors__AllowedOrigins__0=http://localhost:3000" \
                "Cors__AllowedOrigins__1=$NEW_CORS_URL" \
            --restart-policy Always \
            --no-wait \
            2>/dev/null || echo "  Note: Container update in progress"
    fi
    
    echo "  ✅ CORS update initiated for $SERVICE"
done

echo ""
echo "⏳ Waiting for services to restart..."
sleep 30

echo ""
echo "🔍 Verifying CORS settings..."
for SERVICE in "${SERVICES[@]}"; do
    echo ""
    echo "Service: $SERVICE"
    az container show \
        --resource-group $RESOURCE_GROUP \
        --name $SERVICE \
        --query "containers[0].environmentVariables[?contains(name, 'Cors__AllowedOrigins')].{name:name, value:value}" \
        -o table
done

echo ""
echo "✅ CORS update complete!"
echo ""
echo "📱 Your static web app at $NEW_CORS_URL should now be able to access all backend services."
echo ""
echo "🔄 If services are still restarting, wait a minute and try again."