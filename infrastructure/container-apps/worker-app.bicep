@description('Container Apps Environment ID')
param environmentId string

@description('Container Registry Login Server')
param containerRegistryLoginServer string

@description('Managed Identity ID')
param managedIdentityId string

@description('Managed Identity Client ID')
param managedIdentityClientId string

@description('Key Vault name')
param keyVaultName string

@description('Service Bus namespace')
param serviceBusNamespace string

@description('Storage account name')
param storageAccountName string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Image tag')
param imageTag string = 'latest'

@description('Minimum number of replicas')
param minReplicas int = 0

@description('Maximum number of replicas')
param maxReplicas int = 10

// Key Vault reference for secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Worker Container App
resource workerApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: 'voicecode-worker'
  location: resourceGroup().location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        {
          name: 'anthropic-api-key'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/AnthropicApiKey'
          identity: managedIdentityId
        }
        {
          name: 'servicebus-connection'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/ServiceBusConnectionString'
          identity: managedIdentityId
        }
      ]
      registries: [
        {
          server: containerRegistryLoginServer
          identity: managedIdentityId
        }
      ]
      dapr: {
        enabled: true
        appId: 'worker'
        appPort: 80
        appProtocol: 'http'
      }
    }
    template: {
      containers: [
        {
          image: '${containerRegistryLoginServer}/voicecode/worker-service:${imageTag}'
          name: 'worker'
          resources: {
            cpu: json('1.0')
            memory: '2.0Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ANTHROPIC_API_KEY'
              secretRef: 'anthropic-api-key'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: managedIdentityClientId
            }
            {
              name: 'ServiceBus__FullyQualifiedNamespace'
              value: '${serviceBusNamespace}.servicebus.windows.net'
            }
            {
              name: 'ServiceBus__QueueName'
              value: 'claude-code-tasks'
            }
            {
              name: 'ApplicationInsights__ConnectionString'
              value: appInsightsConnectionString
            }
            {
              name: 'ClaudeCode__MaxConcurrentWorkers'
              value: '5'
            }
            {
              name: 'ClaudeCode__WorkspacePath'
              value: '/workspaces'
            }
            {
              name: 'Storage__AccountName'
              value: storageAccountName
            }
          ]
          volumeMounts: [
            {
              volumeName: 'workspaces'
              mountPath: '/workspaces'
            }
          ]
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 80
              }
              initialDelaySeconds: 10
              periodSeconds: 10
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 80
              }
              initialDelaySeconds: 30
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'servicebus-queue-length'
            custom: {
              type: 'azure-servicebus'
              auth: [
                {
                  secretRef: 'servicebus-connection'
                  triggerParameter: 'connection'
                }
              ]
              metadata: {
                queueName: 'claude-code-tasks'
                namespace: serviceBusNamespace
                messageCount: '1' // Scale up when there's at least 1 message
              }
            }
          }
          {
            name: 'cpu-utilization'
            custom: {
              type: 'cpu'
              metadata: {
                type: 'Utilization'
                value: '70' // Scale up at 70% CPU
              }
            }
          }
          {
            name: 'memory-utilization'
            custom: {
              type: 'memory'
              metadata: {
                type: 'Utilization'
                value: '80' // Scale up at 80% memory
              }
            }
          }
        ]
      }
      volumes: [
        {
          name: 'workspaces'
          storageType: 'AzureFile'
          storageName: 'workspaces-storage'
        }
      ]
    }
    workloadProfileName: 'Spot-D4' // Use spot instances for cost optimization
  }
}

// Storage mount for workspaces
resource workspacesStorage 'Microsoft.App/managedEnvironments/storages@2023-11-02-preview' = {
  name: '${split(environmentId, '/')[8]}/workspaces-storage'
  properties: {
    azureFile: {
      accountName: storageAccountName
      shareName: 'workspaces'
      accessMode: 'ReadWrite'
    }
  }
}

output appName string = workerApp.name
output appUrl string = 'https://${workerApp.properties.configuration.ingress.fqdn}'