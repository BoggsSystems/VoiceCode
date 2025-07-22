# VoiceCode Deployment Guide

This guide covers the deployment process for VoiceCode services using CI/CD pipelines.

## Overview

VoiceCode uses automated CI/CD pipelines for building, testing, and deploying services:
- **GitHub Actions** for open-source deployment
- **Azure DevOps** for enterprise deployment
- **Local scripts** for development and manual deployment

## Deployment Environments

### Development
- Automatic deployment on push to `develop` branch
- Runs integration tests before deployment
- Uses development Azure resources
- No manual approval required

### Staging
- Automatic deployment on push to `main` branch
- Full test suite execution
- Uses production-like resources
- Manual approval for production promotion

### Production
- Triggered after staging validation
- Blue-green deployment with slot swapping
- Automated smoke tests
- Rollback capability

## Prerequisites

### Local Development
1. Install dependencies:
   ```bash
   # .NET 8 SDK
   # Docker & Docker Compose
   # Azure CLI
   ```

2. Set up local infrastructure:
   ```bash
   ./scripts/local-development.sh start
   ```

### Azure Resources
1. Azure subscription with appropriate permissions
2. Resource groups created via Terraform
3. Service principals for CI/CD
4. Container Registry access

## CI/CD Pipelines

### GitHub Actions

#### Setup Secrets
Add these secrets to your GitHub repository:
- `AZURE_CREDENTIALS` - Service principal JSON
- `ACR_LOGIN_SERVER` - Container registry URL
- `ACR_USERNAME` - Registry username
- `ACR_PASSWORD` - Registry password
- `CLAUDE_API_KEY` - Claude API key
- `OPENAI_API_KEY` - OpenAI API key (optional)
- `ADMIN_EMAIL` - Admin email for alerts
- `B2C_TENANT_NAME` - Azure AD B2C tenant

#### Pipeline Stages
1. **Build & Test** - Builds solution and runs unit tests
2. **Docker Build** - Creates container images
3. **Deploy Infrastructure** - Updates Terraform resources
4. **Deploy Services** - Deploys to App Services
5. **Integration Tests** - Runs E2E tests
6. **Deploy Static Web App** - Deploys React frontend

### Azure DevOps

#### Setup Service Connections
1. Azure Resource Manager connection
2. Container Registry connection
3. Variable groups for secrets

#### Pipeline Configuration
```yaml
# azure-pipelines.yml configured for:
- Multi-stage deployment
- Parallel service builds
- Approval gates for production
- Automated rollback
```

## Manual Deployment

### Build All Services
```bash
# Build and create Docker images
./scripts/build-all.sh all

# Build only
./scripts/build-all.sh build

# Build Docker images only
./scripts/build-all.sh docker
```

### Deploy to Azure
```bash
# Deploy to development
ENVIRONMENT=dev ./scripts/deploy-services.sh deploy

# Deploy to staging
ENVIRONMENT=staging ./scripts/deploy-services.sh deploy

# Deploy to production (with slot swap)
ENVIRONMENT=prod ./scripts/deploy-services.sh deploy

# Run smoke tests
ENVIRONMENT=prod ./scripts/deploy-services.sh test

# Rollback production
ENVIRONMENT=prod ./scripts/deploy-services.sh rollback
```

## Deployment Process

### 1. Code Changes
```bash
# Create feature branch
git checkout -b feature/new-feature

# Make changes and commit
git add .
git commit -m "Add new feature"

# Push to trigger PR build
git push origin feature/new-feature
```

### 2. Pull Request
- Automated PR validation runs
- Code quality checks
- Security scanning
- Unit tests

### 3. Merge to Main
- Triggers full CI/CD pipeline
- Deploys to staging environment
- Runs integration tests

### 4. Production Deployment
- Manual approval in pipeline
- Blue-green deployment
- Automated smoke tests
- Monitor deployment

## Monitoring Deployment

### Health Checks
All services expose health endpoints:
```bash
# Check service health
curl https://voicecode-prod-app-stt.azurewebsites.net/health
```

### Application Insights
Monitor deployment progress:
1. Check deployment annotations
2. Review performance metrics
3. Monitor error rates
4. Check dependency health

### Smoke Tests
Automated tests verify:
- Service availability
- API connectivity
- Database connections
- External service integration

## Rollback Procedures

### Automatic Rollback
Triggered by:
- Health check failures
- High error rates
- Integration test failures

### Manual Rollback
```bash
# Swap slots back
ENVIRONMENT=prod ./scripts/deploy-services.sh rollback

# Or use Azure CLI
az webapp deployment slot swap \
  --name voicecode-prod-app-stt \
  --resource-group voicecode-prod-rg \
  --slot production \
  --target-slot staging
```

## Troubleshooting

### Common Issues

#### Build Failures
- Check .NET SDK version
- Verify NuGet package restore
- Review build logs

#### Docker Build Issues
- Ensure Dockerfile paths are correct
- Check base image availability
- Verify build context

#### Deployment Failures
- Check Azure credentials
- Verify resource availability
- Review deployment logs

#### Health Check Failures
- Check application logs
- Verify configuration
- Test dependencies

### Debug Commands
```bash
# View service logs
az webapp log tail \
  --name voicecode-prod-app-stt \
  --resource-group voicecode-prod-rg

# Check deployment status
az webapp deployment list \
  --name voicecode-prod-app-stt \
  --resource-group voicecode-prod-rg

# View container logs
az webapp log download \
  --name voicecode-prod-app-stt \
  --resource-group voicecode-prod-rg \
  --log-file logs.zip
```

## Security Considerations

### Secrets Management
- Use Azure Key Vault for production secrets
- Rotate credentials regularly
- Never commit secrets to source control

### Container Security
- Scan images for vulnerabilities
- Use minimal base images
- Keep dependencies updated

### Network Security
- Use private endpoints where possible
- Implement IP restrictions
- Enable HTTPS only

## Performance Optimization

### Build Optimization
- Use Docker layer caching
- Parallelize service builds
- Cache NuGet packages

### Deployment Optimization
- Use deployment slots for zero-downtime
- Pre-warm instances
- Configure auto-scaling

## Maintenance

### Regular Tasks
1. Update dependencies monthly
2. Review and rotate credentials
3. Clean up old container images
4. Monitor resource utilization

### Updating Services
```bash
# Update a single service
docker build -f services/stt-service/Dockerfile -t voicecode-stt-service:new .
az webapp config container set \
  --name voicecode-prod-app-stt \
  --resource-group voicecode-prod-rg \
  --docker-custom-image-name voicecode-stt-service:new
```

## Disaster Recovery

### Backup Strategy
- Automated daily backups
- Geo-redundant storage
- Point-in-time restore capability

### Recovery Procedures
1. Restore from backup
2. Redeploy infrastructure
3. Restore data
4. Validate services