#!/bin/bash

# VoiceCode Terraform Deployment Script
# This script helps deploy the VoiceCode infrastructure using Terraform

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    print_info "Checking prerequisites..."
    
    # Check if Terraform is installed
    if ! command -v terraform &> /dev/null; then
        print_error "Terraform is not installed. Please install Terraform >= 1.5.0"
        exit 1
    fi
    
    # Check if Azure CLI is installed
    if ! command -v az &> /dev/null; then
        print_error "Azure CLI is not installed. Please install Azure CLI"
        exit 1
    fi
    
    # Check if logged in to Azure
    if ! az account show &> /dev/null; then
        print_error "Not logged in to Azure. Please run 'az login'"
        exit 1
    fi
    
    print_info "Prerequisites check passed"
}

# Initialize Terraform backend
init_backend() {
    print_info "Initializing Terraform backend..."
    
    RESOURCE_GROUP="voicecode-terraform-state"
    STORAGE_ACCOUNT="voicecodeterraform"
    CONTAINER="tfstate"
    
    # Check if resource group exists
    if ! az group show --name $RESOURCE_GROUP &> /dev/null; then
        print_info "Creating resource group for Terraform state..."
        az group create --name $RESOURCE_GROUP --location eastus
    fi
    
    # Check if storage account exists
    if ! az storage account show --name $STORAGE_ACCOUNT --resource-group $RESOURCE_GROUP &> /dev/null; then
        print_info "Creating storage account for Terraform state..."
        az storage account create \
            --name $STORAGE_ACCOUNT \
            --resource-group $RESOURCE_GROUP \
            --location eastus \
            --sku Standard_LRS \
            --encryption-services blob
    fi
    
    # Get storage account key
    ACCOUNT_KEY=$(az storage account keys list \
        --resource-group $RESOURCE_GROUP \
        --account-name $STORAGE_ACCOUNT \
        --query '[0].value' -o tsv)
    
    # Check if container exists
    if ! az storage container show --name $CONTAINER --account-name $STORAGE_ACCOUNT --account-key $ACCOUNT_KEY &> /dev/null; then
        print_info "Creating blob container for Terraform state..."
        az storage container create \
            --name $CONTAINER \
            --account-name $STORAGE_ACCOUNT \
            --account-key $ACCOUNT_KEY
    fi
    
    print_info "Backend initialization complete"
}

# Set up environment
setup_environment() {
    local ENV=$1
    
    print_info "Setting up environment: $ENV"
    
    # Check if tfvars file exists
    if [ ! -f "terraform.tfvars" ]; then
        if [ -f "terraform.tfvars.example" ]; then
            print_warn "terraform.tfvars not found. Copying from example..."
            cp terraform.tfvars.example terraform.tfvars
            print_warn "Please edit terraform.tfvars with your values before proceeding"
            exit 1
        else
            print_error "No terraform.tfvars or terraform.tfvars.example found"
            exit 1
        fi
    fi
    
    # Check for required environment variables
    if [ -z "$TF_VAR_claude_api_key" ]; then
        print_error "TF_VAR_claude_api_key environment variable is not set"
        print_info "Please set: export TF_VAR_claude_api_key='your-api-key'"
        exit 1
    fi
}

# Initialize Terraform
init_terraform() {
    print_info "Initializing Terraform..."
    
    cd infrastructure/terraform
    
    terraform init \
        -backend-config="resource_group_name=voicecode-terraform-state" \
        -backend-config="storage_account_name=voicecodeterraform" \
        -backend-config="container_name=tfstate" \
        -backend-config="key=voicecode.tfstate"
}

# Plan Terraform deployment
plan_terraform() {
    local ENV=$1
    
    print_info "Planning Terraform deployment for $ENV..."
    
    terraform plan \
        -var="environment=$ENV" \
        -out=tfplan
}

# Apply Terraform deployment
apply_terraform() {
    print_info "Applying Terraform deployment..."
    
    terraform apply tfplan
    
    # Clean up plan file
    rm -f tfplan
}

# Show outputs
show_outputs() {
    print_info "Deployment complete! Here are the outputs:"
    echo ""
    terraform output
}

# Main execution
main() {
    local COMMAND=$1
    local ENVIRONMENT=${2:-dev}
    
    case $COMMAND in
        "init")
            check_prerequisites
            init_backend
            init_terraform
            ;;
        "plan")
            check_prerequisites
            setup_environment $ENVIRONMENT
            init_terraform
            plan_terraform $ENVIRONMENT
            ;;
        "apply")
            check_prerequisites
            setup_environment $ENVIRONMENT
            init_terraform
            plan_terraform $ENVIRONMENT
            
            print_warn "This will create/modify Azure resources. Continue? (yes/no)"
            read -r CONFIRM
            if [ "$CONFIRM" = "yes" ]; then
                apply_terraform
                show_outputs
            else
                print_info "Deployment cancelled"
                rm -f tfplan
            fi
            ;;
        "destroy")
            check_prerequisites
            setup_environment $ENVIRONMENT
            init_terraform
            
            print_error "WARNING: This will DESTROY all resources!"
            print_warn "Are you absolutely sure? Type 'destroy-$ENVIRONMENT' to confirm:"
            read -r CONFIRM
            if [ "$CONFIRM" = "destroy-$ENVIRONMENT" ]; then
                terraform destroy -var="environment=$ENVIRONMENT"
            else
                print_info "Destroy cancelled"
            fi
            ;;
        "output")
            cd infrastructure/terraform
            terraform output
            ;;
        *)
            echo "Usage: $0 {init|plan|apply|destroy|output} [environment]"
            echo ""
            echo "Commands:"
            echo "  init     - Initialize Terraform backend"
            echo "  plan     - Plan infrastructure changes"
            echo "  apply    - Apply infrastructure changes"
            echo "  destroy  - Destroy all infrastructure"
            echo "  output   - Show Terraform outputs"
            echo ""
            echo "Environments: dev, staging, prod (default: dev)"
            exit 1
            ;;
    esac
}

# Run main function
main "$@"