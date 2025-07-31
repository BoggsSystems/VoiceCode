# Key Vault Integration for Phase-Aware Orchestrator

## Overview

The Phase-Aware Orchestrator Service uses Azure Key Vault to securely manage the OpenAI API key, following the same pattern as other VoiceCode services.

## Configuration Flow

### 1. Key Vault Secret Storage
The OpenAI API key is stored in Azure Key Vault as a secret named `openai-api-key`.

### 2. Container App Configuration
The orchestrator Container App is configured to:
- Use a managed identity to access Key Vault
- Define a secret reference in the Container App that points to the Key Vault secret
- Inject the secret as an environment variable

### 3. Application Configuration
The ChatGPTService reads the API key from configuration:
```csharp
_apiKey = configuration["OpenAI:ApiKey"];
```

ASP.NET Core automatically maps the environment variable `OpenAI__ApiKey` to the configuration path `OpenAI:ApiKey`.

## Infrastructure Setup

### Bicep Configuration (orchestrator-app.bicep)
```bicep
secrets: [
  {
    name: 'openai-api-key'
    keyVaultUrl: '${keyVault.properties.vaultUri}secrets/openai-api-key'
    identity: managedIdentityId
  }
]

env: [
  {
    name: 'OpenAI__ApiKey'
    secretRef: 'openai-api-key'
  }
]
```

### Deployment Process
1. The deployment script checks if the OpenAI API key exists in Key Vault
2. If not found, it prompts for the key and stores it in Key Vault
3. The Container App pulls the secret from Key Vault using its managed identity
4. The secret is injected as an environment variable at runtime

## Security Benefits

1. **No hardcoded secrets**: API keys are never stored in code or configuration files
2. **Centralized management**: All secrets are managed in one place
3. **Access control**: Only authorized identities can access the secrets
4. **Audit trail**: Key Vault provides logging of all secret access
5. **Rotation support**: Secrets can be updated in Key Vault without code changes

## Deployment Commands

### Store OpenAI API Key in Key Vault
```bash
az keyvault secret set \
  --vault-name voicecode-keyvault \
  --name openai-api-key \
  --value "sk-..."
```

### Deploy Orchestrator with Key Vault Integration
```bash
./deploy-phase-aware.sh
```

## Troubleshooting

### Common Issues

1. **"OpenAI API key not found in configuration"**
   - Ensure the secret exists in Key Vault
   - Verify the Container App has access to Key Vault
   - Check the managed identity permissions

2. **"Access denied to Key Vault"**
   - Ensure the managed identity has "Secret Get" permission on Key Vault
   - Verify the Key Vault name is correct

3. **"Invalid API key" from OpenAI**
   - Verify the key stored in Key Vault is correct
   - Check that the key hasn't expired

### Verification Commands

Check if secret exists:
```bash
az keyvault secret show \
  --vault-name voicecode-keyvault \
  --name openai-api-key
```

Check Container App environment variables:
```bash
az containerapp show \
  --name voicecode-orchestrator \
  --resource-group voicecode-rg \
  --query properties.template.containers[0].env
```

## Best Practices

1. Never log or output the API key value
2. Use separate Key Vaults for different environments (dev, staging, prod)
3. Regularly rotate API keys
4. Monitor Key Vault access logs
5. Use Azure Policy to enforce Key Vault usage