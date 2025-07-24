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

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Image tag')
param imageTag string = 'latest'

// Key Vault reference for secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Orchestrator Container App
resource orchestratorApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: 'voicecode-orchestrator'
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
      ingress: {
        external: true
        targetPort: 80
        transport: 'http'
        corsPolicy: {
          allowedOrigins: [
            'https://voicecode.dev'
            'http://localhost:3000'
          ]
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
          allowedHeaders: ['*']
          allowCredentials: true
        }
      }
      dapr: {
        enabled: true
        appId: 'orchestrator'
        appPort: 80
        appProtocol: 'http'
      }
    }
    template: {
      containers: [
        {
          image: '${containerRegistryLoginServer}/voicecode/orchestrator-service:${imageTag}'
          name: 'orchestrator'
          resources: {
            cpu: json('0.5')
            memory: '1.0Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
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
              name: 'ApplicationInsights__ConnectionString'
              value: appInsightsConnectionString
            }
            {
              name: 'Orchestration__EnableMultiAgent'
              value: 'true'
            }
            {
              name: 'Orchestration__MaxConcurrentWorkers'
              value: '10'
            }
            {
              name: 'ServiceEndpoints__WorkerService'
              value: 'http://worker.internal.${split(environmentId, '/')[8]}.azurecontainerapps.io'
            }
            {
              name: 'ServiceEndpoints__STTService'
              value: 'https://voicecode-stt.internal.${split(environmentId, '/')[8]}.azurecontainerapps.io'
            }
            {
              name: 'ServiceEndpoints__TTSService'
              value: 'https://voicecode-tts.internal.${split(environmentId, '/')[8]}.azurecontainerapps.io'
            }
            {
              name: 'ServiceEndpoints__ClaudeService'
              value: 'https://voicecode-claude.internal.${split(environmentId, '/')[8]}.azurecontainerapps.io'
            }
            {
              name: 'ServiceEndpoints__GeneratorService'
              value: 'https://voicecode-generator.internal.${split(environmentId, '/')[8]}.azurecontainerapps.io'
            }
            {
              name: 'ServiceEndpoints__RouterService'
              value: 'https://voicecode-router.internal.${split(environmentId, '/')[8]}.azurecontainerapps.io'
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
        minReplicas: 1 // Always have at least one orchestrator
        maxReplicas: 3
        rules: [
          {
            name: 'http-requests'
            http: {
              metadata: {
                concurrentRequests: '10' // Scale up with 10+ concurrent requests
              }
            }
          }
          {
            name: 'cpu-utilization'
            custom: {
              type: 'cpu'
              metadata: {
                type: 'Utilization'
                value: '60' // Scale up at 60% CPU
              }
            }
          }
        ]
      }
    }
    workloadProfileName: 'Consumption' // Use consumption plan for orchestrator
  }
}

output appName string = orchestratorApp.name
output appUrl string = 'https://${orchestratorApp.properties.configuration.ingress.fqdn}'