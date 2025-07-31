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
param minReplicas int = 1

@description('Maximum number of replicas')
param maxReplicas int = 3

// Key Vault reference for secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Observer Container App
resource observerApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: 'voicecode-observer'
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
      registries: [
        {
          server: containerRegistryLoginServer
          identity: managedIdentityId
        }
      ]
      secrets: [
        {
          name: 'openai-api-key'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/openai-api-key'
          identity: managedIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          image: '${containerRegistryLoginServer}/voicecode-observer:${imageTag}'
          name: 'observer'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ApplicationInsights__ConnectionString'
              value: appInsightsConnectionString
            }
            {
              name: 'ServiceBus__ConnectionString'
              value: 'https://${serviceBusNamespace}.servicebus.windows.net'
            }
            {
              name: 'Observer__ServiceName'
              value: 'ObserverService'
            }
            {
              name: 'Observer__MaxConcurrentStreams'
              value: '10'
            }
            {
              name: 'Observer__NarrationThrottleMs'
              value: '2000'
            }
            {
              name: 'ServiceBus__StreamQueueName'
              value: 'sdk-stream-events'
            }
            {
              name: 'ServiceBus__NarrationQueueName'
              value: 'tts-requests'
            }
            {
              name: 'OpenAI__ApiKey'
              secretRef: 'openai-api-key'
            }
            {
              name: 'OpenAI__Endpoint'
              value: 'https://voicecode-openai.openai.azure.com/'
            }
            {
              name: 'OpenAI__DeploymentName'
              value: 'gpt-4'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: managedIdentityClientId
            }
          ]
          probes: [
            {
              type: 'liveness'
              httpGet: {
                path: '/health'
                port: 8080
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
            name: 'servicebus-scale-rule'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                queueName: 'sdk-stream-events'
                namespace: serviceBusNamespace
                messageCount: '5'
              }
              auth: [
                {
                  secretRef: 'servicebus-connection-string'
                  triggerParameter: 'connection'
                }
              ]
            }
          }
        ]
      }
    }
  }
}

output observerAppFqdn string = observerApp.properties.configuration.ingress.fqdn
output observerAppName string = observerApp.name