# VoiceCode Container Instances Deployment Summary

## Deployment Status

All 6 services have been successfully deployed to Azure Container Instances:

### Service URLs
- **Claude Service**: https://voicecode-dev-eus-claude.eastus.azurecontainer.io
- **STT Service**: https://voicecode-dev-eus-stt.eastus.azurecontainer.io
- **TTS Service**: https://voicecode-dev-eus-tts.eastus.azurecontainer.io
- **Dispatcher Service**: https://voicecode-dev-eus-dispatcher.eastus.azurecontainer.io
- **Generator Service**: https://voicecode-dev-eus-generator.eastus.azurecontainer.io
- **Router Service**: https://voicecode-dev-eus-router.eastus.azurecontainer.io

### Container Details
- **Resource Group**: voicecode-dev-eus-rg
- **Location**: East US
- **Container Registry**: voicecodebuildsprod.azurecr.io
- **Image Tag**: v3 (latest for some services)

### Resource Allocation
- **Claude & Generator**: 1.0 CPU, 2.0 GB Memory
- **Other Services**: 0.5 CPU, 1.0 GB Memory

## Current Issues

Some services are experiencing Key Vault access issues. The services are trying to connect to Key Vault but failing with authentication errors.

### Health Check Status
- ✅ Router: 200 (Healthy)
- ⚠️ STT: 503 (Service Unavailable)
- ⚠️ TTS: 503 (Service Unavailable)
- ❌ Claude: Connection Failed
- ❌ Dispatcher: Connection Failed
- ❌ Generator: Connection Failed

## API Keys Configured

The following API keys have been added to Azure Key Vault:
- **ClaudeApiKey**: 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
- **OpenAIApiKey**: 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

## Troubleshooting Steps

### To Check Container Logs
```bash
# View logs for a specific service
az container logs --resource-group voicecode-dev-eus-rg --name voicecode-dev-eus-<service>-ci

# Example for Claude service
az container logs --resource-group voicecode-dev-eus-rg --name voicecode-dev-eus-claude-ci
```

### To Restart Containers
```bash
# Restart a specific container
az container restart --resource-group voicecode-dev-eus-rg --name voicecode-dev-eus-<service>-ci

# Restart all containers
./restart-containers.sh
```

### To Update Container Configuration
The containers need proper managed identity configuration to access Key Vault. This requires:
1. Creating user-assigned managed identities for each service
2. Granting Key Vault access policies to these identities
3. Updating containers to use these identities

## Next Steps

1. **Fix Key Vault Access**: The containers need managed identities with proper Key Vault permissions
2. **Update Environment Variables**: Some services may need additional configuration
3. **Verify Network Connectivity**: Ensure services can communicate with each other
4. **Monitor Logs**: Check container logs for specific error messages

## Scripts Created

- `deploy-container-instances.sh` - Original deployment script
- `deploy-aci-with-identity.sh` - Deployment with managed identity support
- `deploy-aci-simple.sh` - Simplified deployment script
- `update-aci-services.sh` - Update existing containers
- `update-existing-containers.sh` - Check and update container configuration
- `restart-containers.sh` - Restart all containers

All scripts are located in `/Users/jeffboggs/VoiceCode/infrastructure/scripts/`