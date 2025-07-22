#!/bin/bash

# VoiceCode Service Deployment Script
# Deploys services to Azure Web Apps

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
ENVIRONMENT="${ENVIRONMENT:-dev}"
RESOURCE_GROUP="voicecode-${ENVIRONMENT}-rg"
ACR_NAME="voicecode${ENVIRONMENT}acr"
APP_PREFIX="voicecode-${ENVIRONMENT}-app"

# Services to deploy
SERVICES=(
    "stt"
    "claude"
    "router"
    "generator"
    "tts"
    "dispatcher"
)

# Function to print colored output
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if logged into Azure
check_azure_login() {
    print_info "Checking Azure login status..."
    
    if ! az account show &> /dev/null; then
        print_error "Not logged into Azure. Please run 'az login'"
        exit 1
    fi
    
    SUBSCRIPTION=$(az account show --query name -o tsv)
    print_info "Using subscription: $SUBSCRIPTION"
}

# Login to ACR
login_to_acr() {
    print_info "Logging into Azure Container Registry..."
    
    az acr login --name $ACR_NAME
    
    if [ $? -eq 0 ]; then
        print_success "Logged into ACR successfully"
    else
        print_error "Failed to login to ACR"
        exit 1
    fi
}

# Deploy a single service
deploy_service() {
    local service=$1
    local app_name="${APP_PREFIX}-${service}"
    local image="${ACR_NAME}.azurecr.io/voicecode-${service}-service:latest"
    
    print_info "Deploying $service to $app_name..."
    
    # Update app service with new image
    az webapp config container set \
        --name $app_name \
        --resource-group $RESOURCE_GROUP \
        --docker-custom-image-name $image \
        --docker-registry-server-url "https://${ACR_NAME}.azurecr.io"
    
    # Restart the app service
    az webapp restart \
        --name $app_name \
        --resource-group $RESOURCE_GROUP
    
    # Wait for deployment to complete
    print_info "Waiting for $service to be healthy..."
    
    local max_attempts=30
    local attempt=0
    
    while [ $attempt -lt $max_attempts ]; do
        local health_status=$(az webapp show \
            --name $app_name \
            --resource-group $RESOURCE_GROUP \
            --query "state" -o tsv)
        
        if [ "$health_status" == "Running" ]; then
            # Check health endpoint
            local health_url="https://${app_name}.azurewebsites.net/health"
            local http_status=$(curl -s -o /dev/null -w "%{http_code}" $health_url || echo "000")
            
            if [ "$http_status" == "200" ]; then
                print_success "$service deployed and healthy"
                return 0
            fi
        fi
        
        attempt=$((attempt + 1))
        sleep 10
    done
    
    print_error "Deployment of $service failed or timed out"
    return 1
}

# Deploy to staging slot
deploy_to_staging() {
    local service=$1
    local app_name="${APP_PREFIX}-${service}"
    local image="${ACR_NAME}.azurecr.io/voicecode-${service}-service:latest"
    
    print_info "Deploying $service to staging slot..."
    
    # Deploy to staging slot
    az webapp deployment slot create \
        --name $app_name \
        --resource-group $RESOURCE_GROUP \
        --slot staging \
        --configuration-source $app_name \
        2>/dev/null || true
    
    # Update staging slot with new image
    az webapp config container set \
        --name $app_name \
        --resource-group $RESOURCE_GROUP \
        --slot staging \
        --docker-custom-image-name $image \
        --docker-registry-server-url "https://${ACR_NAME}.azurecr.io"
    
    print_success "$service deployed to staging"
}

# Swap staging to production
swap_slots() {
    local service=$1
    local app_name="${APP_PREFIX}-${service}"
    
    print_info "Swapping staging to production for $service..."
    
    az webapp deployment slot swap \
        --name $app_name \
        --resource-group $RESOURCE_GROUP \
        --slot staging \
        --target-slot production
    
    print_success "$service swapped to production"
}

# Run smoke tests
run_smoke_tests() {
    print_info "Running smoke tests..."
    
    local all_healthy=true
    
    for service in "${SERVICES[@]}"; do
        local app_name="${APP_PREFIX}-${service}"
        local health_url="https://${app_name}.azurewebsites.net/health"
        
        print_info "Testing $service health endpoint..."
        local http_status=$(curl -s -o /dev/null -w "%{http_code}" $health_url || echo "000")
        
        if [ "$http_status" == "200" ]; then
            print_success "$service is healthy"
        else
            print_error "$service health check failed (HTTP $http_status)"
            all_healthy=false
        fi
    done
    
    if [ "$all_healthy" == "true" ]; then
        print_success "All services are healthy"
        return 0
    else
        print_error "Some services failed health checks"
        return 1
    fi
}

# Update App Settings
update_app_settings() {
    local service=$1
    local app_name="${APP_PREFIX}-${service}"
    
    print_info "Updating app settings for $service..."
    
    # Common settings for all services
    az webapp config appsettings set \
        --name $app_name \
        --resource-group $RESOURCE_GROUP \
        --settings \
            "ASPNETCORE_ENVIRONMENT=${ENVIRONMENT^}" \
            "ApplicationInsights__ConnectionString=@Microsoft.KeyVault(VaultName=voicecode${ENVIRONMENT}kv;SecretName=ApplicationInsightsConnectionString)" \
            "KeyVaultName=voicecode${ENVIRONMENT}kv"
}

# Main deployment function
deploy_all() {
    print_info "Deploying all services to $ENVIRONMENT environment"
    
    check_azure_login
    login_to_acr
    
    local failed_services=()
    
    for service in "${SERVICES[@]}"; do
        if [ "$ENVIRONMENT" == "prod" ]; then
            # Deploy to staging first in production
            deploy_to_staging "$service"
        else
            # Direct deployment for non-prod
            if ! deploy_service "$service"; then
                failed_services+=("$service")
            fi
        fi
        
        # Update app settings
        update_app_settings "$service"
    done
    
    # If production, swap slots after all staging deployments
    if [ "$ENVIRONMENT" == "prod" ]; then
        print_info "All services deployed to staging. Ready to swap?"
        read -p "Swap staging to production? (yes/no): " confirm
        
        if [ "$confirm" == "yes" ]; then
            for service in "${SERVICES[@]}"; do
                swap_slots "$service"
            done
        else
            print_warning "Deployment to staging complete. Swap cancelled."
        fi
    fi
    
    # Run smoke tests
    run_smoke_tests
    
    if [ ${#failed_services[@]} -eq 0 ]; then
        print_success "Deployment completed successfully!"
    else
        print_error "Failed to deploy: ${failed_services[*]}"
        exit 1
    fi
}

# Main execution
main() {
    local COMMAND=${1:-deploy}
    
    case $COMMAND in
        "deploy")
            deploy_all
            ;;
            
        "test")
            check_azure_login
            run_smoke_tests
            ;;
            
        "rollback")
            if [ "$ENVIRONMENT" != "prod" ]; then
                print_error "Rollback only available in production"
                exit 1
            fi
            
            print_info "Rolling back production deployment..."
            for service in "${SERVICES[@]}"; do
                local app_name="${APP_PREFIX}-${service}"
                az webapp deployment slot swap \
                    --name $app_name \
                    --resource-group $RESOURCE_GROUP \
                    --slot production \
                    --target-slot staging
                print_success "$service rolled back"
            done
            ;;
            
        *)
            echo "Usage: $0 {deploy|test|rollback}"
            echo ""
            echo "Commands:"
            echo "  deploy   - Deploy all services"
            echo "  test     - Run smoke tests"
            echo "  rollback - Rollback production deployment"
            echo ""
            echo "Environment: $ENVIRONMENT (set ENVIRONMENT variable to change)"
            exit 1
            ;;
    esac
}

# Run main function
main "$@"