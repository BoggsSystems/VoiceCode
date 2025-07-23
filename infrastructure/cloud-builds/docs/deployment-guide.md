# VoiceCode Cloud Builds Deployment Guide

This guide provides step-by-step instructions for deploying the VoiceCode cloud build infrastructure to Azure.

## Prerequisites

- Azure CLI installed and configured
- Terraform >= 1.0
- Docker for building container images
- GitHub repository with Actions enabled
- Azure subscription with appropriate permissions

## 1. Initial Setup

### 1.1 Clone Repository and Navigate to Infrastructure

```bash
git clone https://github.com/your-org/VoiceCode.git
cd VoiceCode/infrastructure/cloud-builds
```

### 1.2 Configure Azure Authentication

```bash
# Login to Azure
az login

# Set subscription (if you have multiple)
az account set --subscription "your-subscription-id"

# Create service principal for Terraform
az ad sp create-for-rbac --name "voicecode-terraform" \
  --role="Contributor" \
  --scopes="/subscriptions/your-subscription-id"
```

### 1.3 Set Environment Variables

```bash
export AZURE_SUBSCRIPTION_ID="your-subscription-id"
export AZURE_CLIENT_ID="service-principal-client-id"
export AZURE_CLIENT_SECRET="service-principal-secret"
export AZURE_TENANT_ID="your-tenant-id"
export GITHUB_TOKEN="your-github-token"
export GITHUB_OWNER="your-github-username"
```

## 2. Deploy Infrastructure with Terraform

### 2.1 Initialize Terraform

```bash
cd terraform
terraform init
```

### 2.2 Create Terraform Variables File

```bash
cat > terraform.tfvars << EOF
resource_group_name = "voicecode-rg"
location = "East US"
environment = "prod"
github_token = "$GITHUB_TOKEN"
github_owner = "$GITHUB_OWNER"
github_repository = "VoiceCode"
EOF
```

### 2.3 Plan and Apply Infrastructure

```bash
# Review the plan
terraform plan

# Apply the infrastructure
terraform apply
```

### 2.4 Note Output Values

```bash
# Get important output values
terraform output container_registry_login_server
terraform output build_storage_account_name
terraform output build_identity_client_id
```

## 3. Build and Push Container Images

### 3.1 Login to Container Registry

```bash
# Get registry credentials from Terraform output
REGISTRY_SERVER=$(terraform output -raw container_registry_login_server)

# Login to registry
az acr login --name $(echo $REGISTRY_SERVER | cut -d'.' -f1)
```

### 3.2 Build Base Image

```bash
cd ../build-agents/base

# Build base image
docker build -t $REGISTRY_SERVER/voicecode-build-base:latest .

# Push base image
docker push $REGISTRY_SERVER/voicecode-build-base:latest
```

### 3.3 Build Language-Specific Images

```bash
# Build .NET image
cd ../dotnet
docker build -t $REGISTRY_SERVER/voicecode-build-dotnet:latest .
docker push $REGISTRY_SERVER/voicecode-build-dotnet:latest

# Build Node.js image
cd ../nodejs
docker build -t $REGISTRY_SERVER/voicecode-build-nodejs:latest .
docker push $REGISTRY_SERVER/voicecode-build-nodejs:latest
```

### 3.4 Build Build Coordinator

```bash
cd ../coordinator
docker build -t $REGISTRY_SERVER/voicecode-build-coordinator:latest .
docker push $REGISTRY_SERVER/voicecode-build-coordinator:latest
```

## 4. Configure GitHub Actions

### 4.1 Copy Workflow Files

```bash
# Copy workflows to .github/workflows
cp ../workflows/*.yml ../../../.github/workflows/
```

### 4.2 Verify GitHub Secrets

Ensure the following secrets are configured in your GitHub repository:

- `AZURE_CLIENT_ID`
- `AZURE_CLIENT_SECRET`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_TENANT_ID`
- `ACR_LOGIN_SERVER`
- `BUILD_STORAGE_ACCOUNT`

These should be automatically created by Terraform.

### 4.3 Test GitHub Actions

```bash
# Trigger image build workflow
gh workflow run build-images.yml

# Check workflow status
gh run list --workflow=build-images.yml
```

## 5. Deploy Container Instances

### 5.1 Apply Container Instance Configuration

```bash
cd ../terraform
terraform apply -target=azurerm_container_group.build_instance
terraform apply -target=azurerm_container_group.build_pool
```

### 5.2 Verify Deployment

```bash
# Check container instances
az container list --resource-group voicecode-rg --output table

# Check logs
az container logs --resource-group voicecode-rg --name voicecode-build-instance-prod
```

## 6. Configure Build Management

### 6.1 Set Up Build Manager

```bash
cd ../scripts

# Make scripts executable
chmod +x build-manager.sh container-management.sh

# Test build manager
./build-manager.sh help
```

### 6.2 Configure Build Settings

```bash
# Edit build configuration
vi ../config/build-config.json

# Validate configuration
jq empty ../config/build-config.json && echo "Valid JSON"
```

## 7. Test Build System

### 7.1 Submit Test Build

```bash
# Create test build request
cat > test-build.json << EOF
{
  "source_url": "https://github.com/microsoft/vscode.git",
  "source_type": "git",
  "project_type": "nodejs",
  "build_config": "Production",
  "user_id": "test-user-123"
}
EOF

# Submit build
./scripts/build-manager.sh submit "$(cat test-build.json)"
```

### 7.2 Monitor Test Build

```bash
# Trigger GitHub Actions workflow
gh workflow run user-build.yml \
  --field source_url="https://github.com/microsoft/vscode.git" \
  --field source_type="git" \
  --field project_type="nodejs" \
  --field build_config="Production" \
  --field user_id="test-user-123"

# Monitor workflow
gh run watch
```

## 8. Configure Monitoring and Alerts

### 8.1 Set Up Log Analytics

```bash
# Verify Log Analytics workspace
az monitor log-analytics workspace show \
  --resource-group voicecode-rg \
  --workspace-name voicecode-build-logs-prod
```

### 8.2 Configure Alerts

```bash
# Test alert configuration
az monitor metrics alert show \
  --resource-group voicecode-rg \
  --name build-queue-length-alert-prod
```

## 9. Production Configuration

### 9.1 Security Hardening

```bash
# Rotate secrets
az ad sp credential reset --name "voicecode-terraform"

# Update Key Vault secrets
az keyvault secret set \
  --vault-name voicecode-build-kv-prod \
  --name "build-secret" \
  --value "new-secret-value"
```

### 9.2 Performance Tuning

```bash
# Scale build pool
./scripts/container-management.sh scale 10

# Configure auto-scaling (manual step - set up Azure Monitor rules)
```

### 9.3 Backup Configuration

```bash
# Export Terraform state
terraform show -json > terraform-state-backup.json

# Backup configuration
tar -czf voicecode-build-config-$(date +%Y%m%d).tar.gz \
  config/ scripts/ terraform/ build-agents/
```

## 10. Maintenance

### 10.1 Regular Cleanup

```bash
# Clean up old containers (dry run first)
./scripts/container-management.sh cleanup 24 true
./scripts/container-management.sh cleanup 24 false

# Clean up old artifacts
az storage blob delete-batch \
  --account-name voicecodebuildprod \
  --source build-artifacts \
  --pattern "builds/*/artifacts.tar.gz" \
  --if-modified-since "7 days ago"
```

### 10.2 Updates

```bash
# Update container images
docker pull mcr.microsoft.com/dotnet/sdk:8.0
docker pull node:20-alpine

# Rebuild and push images
./scripts/build-images.sh

# Update Terraform infrastructure
terraform plan
terraform apply
```

## Troubleshooting

### Common Issues

1. **Container Creation Fails**
   ```bash
   # Check resource quotas
   az vm list-usage --location "East US" --output table
   
   # Check container logs
   az container logs --resource-group voicecode-rg --name container-name
   ```

2. **Build Timeouts**
   ```bash
   # Increase timeout in config
   jq '.default_timeout_minutes = 60' config/build-config.json > temp.json
   mv temp.json config/build-config.json
   ```

3. **Registry Access Issues**
   ```bash
   # Check registry permissions
   az acr check-health --name voicecodebuilds
   
   # Regenerate credentials
   az acr credential renew --name voicecodebuilds --password-name password
   ```

### Logs and Diagnostics

```bash
# View build coordinator logs
az container logs --resource-group voicecode-rg --name voicecode-build-instance-prod

# Check Service Bus metrics
az monitor metrics list \
  --resource /subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/voicecode-rg/providers/Microsoft.ServiceBus/namespaces/voicecode-builds-prod \
  --metric "ActiveMessages"

# Query Log Analytics
az monitor log-analytics query \
  --workspace voicecode-build-logs-prod \
  --analytics-query "ContainerInstanceLog_CL | where TimeGenerated > ago(1h) | order by TimeGenerated desc"
```

## Next Steps

After successful deployment:

1. Configure integration with VoiceCode services
2. Set up user authentication and authorization
3. Implement billing and usage tracking
4. Configure advanced monitoring and dashboards
5. Set up disaster recovery procedures

For more information, see:
- [Build Agent Development Guide](build-agent-guide.md)
- [Security Best Practices](security-guide.md)
- [Monitoring and Alerting](monitoring-guide.md)