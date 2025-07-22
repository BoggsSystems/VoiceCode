# VoiceCode Azure Infrastructure (Terraform)

This directory contains Terraform configurations for deploying the complete VoiceCode infrastructure on Microsoft Azure.

## Architecture Overview

The infrastructure includes:
- **App Services**: Linux-based Web Apps for all microservices
- **API Management**: Centralized API gateway
- **Service Bus**: Message queuing for async communication
- **Redis Cache**: Distributed caching and SignalR backplane
- **Cosmos DB**: NoSQL database for sessions and projects
- **Storage Account**: Blob storage for audio and generated files
- **Key Vault**: Secure secret management
- **Application Insights**: Monitoring and diagnostics
- **Container Registry**: Docker image storage
- **Static Web App**: React frontend hosting

## Prerequisites

1. Azure CLI installed and authenticated
2. Terraform >= 1.5.0
3. Azure subscription with appropriate permissions
4. Service Principal or User with Contributor access

## Setup Instructions

### 1. Initialize Backend Storage

Create storage account for Terraform state:

```bash
# Create resource group
az group create --name voicecode-terraform-state --location eastus

# Create storage account
az storage account create \
  --name voicecodeterraform \
  --resource-group voicecode-terraform-state \
  --location eastus \
  --sku Standard_LRS

# Create container
az storage container create \
  --name tfstate \
  --account-name voicecodeterraform
```

### 2. Configure Variables

Copy and update the example variables file:

```bash
cp terraform.tfvars.example terraform.tfvars
```

Edit `terraform.tfvars` with your values:
- Set `admin_email` to your email
- Set `b2c_tenant_name` to your B2C tenant
- Add API keys as environment variables or in Key Vault

### 3. Set API Keys

Export sensitive variables as environment variables:

```bash
export TF_VAR_claude_api_key="your-claude-api-key"
export TF_VAR_openai_api_key="your-openai-api-key"  # Optional
```

### 4. Deploy Infrastructure

```bash
# Initialize Terraform
terraform init

# Review the plan
terraform plan

# Apply the configuration
terraform apply
```

## Environment-Specific Deployments

### Development
```bash
terraform workspace new dev
terraform apply -var="environment=dev"
```

### Staging
```bash
terraform workspace new staging
terraform apply -var="environment=staging"
```

### Production
```bash
terraform workspace new prod
terraform apply -var="environment=prod" \
  -var="service_plan_sku=P2v3" \
  -var="service_plan_capacity=3" \
  -var="redis_sku.name=Premium" \
  -var="redis_sku.family=P" \
  -var="redis_sku.capacity=1"
```

## Post-Deployment Steps

### 1. Configure Azure AD B2C

1. Create Azure AD B2C tenant
2. Register applications for each service
3. Create user flows:
   - Sign up and sign in
   - Password reset
   - Profile editing

### 2. Deploy Application Code

1. Build Docker images:
```bash
docker build -f services/stt-service/Dockerfile -t voicecode-stt-service .
docker build -f services/claude-service/Dockerfile -t voicecode-claude-service .
# ... repeat for all services
```

2. Push to Container Registry:
```bash
az acr login --name $(terraform output -raw container_registry_url)
docker tag voicecode-stt-service $(terraform output -raw container_registry_url)/voicecode-stt-service:latest
docker push $(terraform output -raw container_registry_url)/voicecode-stt-service:latest
# ... repeat for all services
```

3. Deploy to App Services (via Azure DevOps or GitHub Actions)

### 3. Configure Static Web App

1. Set environment variables in Azure Portal:
```javascript
REACT_APP_API_URL=$(terraform output -raw api_management_gateway_url)
REACT_APP_SIGNALR_URL=$(terraform output -json app_service_urls | jq -r '.dispatcher')
REACT_APP_CLIENT_ID=$(terraform output -json azure_ad_app_ids | jq -r '.webapp')
```

2. Deploy React app through GitHub integration

### 4. Configure Custom Domain (Optional)

1. Add custom domain to API Management
2. Configure SSL certificate
3. Update DNS records

## Resource Naming Convention

Resources follow the pattern: `{project}-{environment}-{location}-{resource}`

Examples:
- `voicecode-dev-eus-rg` (Resource Group)
- `voicecode-dev-eus-app-stt` (STT App Service)
- `voicecodedevkveus` (Key Vault - no hyphens allowed)

## Cost Optimization

### Development Environment
- Use Free/Basic tiers where possible
- Scale down to 1 instance
- Disable autoscaling

### Production Environment
- Enable autoscaling
- Use Premium tiers for critical services
- Enable geo-replication for Cosmos DB
- Use Premium Redis with clustering

## Monitoring and Alerts

The infrastructure includes:
- CPU and Memory alerts for App Services
- Dead letter queue alerts for Service Bus
- Redis memory usage alerts
- Cosmos DB RU consumption alerts
- HTTP error rate alerts

Alerts are sent to the configured admin email.

## Security Considerations

1. **Network Security**:
   - Private endpoints available (set `enable_private_endpoints = true`)
   - IP restrictions configurable
   - TLS 1.2 minimum

2. **Identity & Access**:
   - Managed identities for all services
   - Key Vault for secret management
   - RBAC assignments for resource access

3. **Data Protection**:
   - Encryption at rest enabled
   - Soft delete for Key Vault
   - Backup retention configured

## Troubleshooting

### Common Issues

1. **Terraform state lock**: 
   ```bash
   terraform force-unlock <lock-id>
   ```

2. **Service Principal permissions**:
   Ensure SP has "User Access Administrator" role for RBAC assignments

3. **Key Vault access**:
   Add your IP to `allowed_ip_ranges` during deployment

### Useful Commands

```bash
# List all resources
terraform state list

# Show specific resource
terraform state show azurerm_linux_web_app.services["stt"]

# Import existing resource
terraform import azurerm_resource_group.main /subscriptions/{sub-id}/resourceGroups/{rg-name}

# Destroy specific resource
terraform destroy -target=azurerm_linux_web_app.services["stt"]
```

## Cleanup

To destroy all resources:

```bash
terraform destroy
```

⚠️ **Warning**: This will delete all resources and data. Ensure backups are taken first.

## Module Structure

```
terraform/
├── versions.tf          # Provider versions
├── providers.tf         # Provider configuration
├── variables.tf         # Input variables
├── locals.tf           # Local values
├── main.tf             # Core infrastructure
├── app-services.tf     # App Service resources
├── identity.tf         # Azure AD configuration
├── api-management.tf   # API Management setup
├── secrets.tf          # Key Vault secrets
├── monitoring.tf       # Alerts and diagnostics
├── outputs.tf          # Output values
└── terraform.tfvars    # Variable values (gitignored)
```