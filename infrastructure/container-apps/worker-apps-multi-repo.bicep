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

// Repository assignments for each worker
var workerConfigs = [
  {
    workerId: 'worker-1'
    name: 'voicecode-worker-restaurant'
    repo: 'https://github.com/restaurantchain/pos-system'
    queueName: 'worker-1-tasks'
  }
  {
    workerId: 'worker-2'
    name: 'voicecode-worker-lawfirm'
    repo: 'https://github.com/lawfirm/case-management'
    queueName: 'worker-2-tasks'
  }
  {
    workerId: 'worker-3'
    name: 'voicecode-worker-startup'
    repo: 'https://github.com/startupx/saas-platform'
    queueName: 'worker-3-tasks'
  }
  {
    workerId: 'worker-4'
    name: 'voicecode-worker-bank'
    repo: 'https://github.com/localbank/mobile-banking'
    queueName: 'worker-4-tasks'
  }
  {
    workerId: 'worker-5'
    name: 'voicecode-worker-ecommerce'
    repo: 'https://github.com/ecomstore/shopping-platform'
    queueName: 'worker-5-tasks'
  }
  {
    workerId: 'worker-6'
    name: 'voicecode-worker-health'
    repo: 'https://github.com/healthclinic/patient-portal'
    queueName: 'worker-6-tasks'
  }
  {
    workerId: 'worker-7'
    name: 'voicecode-worker-realestate'
    repo: 'https://github.com/realestate/property-listings'
    queueName: 'worker-7-tasks'
  }
  {
    workerId: 'worker-8'
    name: 'voicecode-worker-school'
    repo: 'https://github.com/schooldistrict/parent-portal'
    queueName: 'worker-8-tasks'
  }
  {
    workerId: 'worker-9'
    name: 'voicecode-worker-fitness'
    repo: 'https://github.com/fitnessapp/workout-tracker'
    queueName: 'worker-9-tasks'
  }
  {
    workerId: 'worker-10'
    name: 'voicecode-worker-nonprofit'
    repo: 'https://github.com/nonprofit/donation-platform'
    queueName: 'worker-10-tasks'
  }
]

// Key Vault reference for secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Deploy a Container App for each worker
resource workerApps 'Microsoft.App/containerApps@2023-11-02-preview' = [for config in workerConfigs: {
  name: config.name
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
          name: 'github-token'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/GitHubToken'
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
        appId: config.workerId
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
              name: 'WORKER_ID'
              value: config.workerId
            }
            {
              name: 'ASSIGNED_REPO'
              value: config.repo
            }
            {
              name: 'GITHUB_TOKEN'
              secretRef: 'github-token'
            }
            {
              name: 'GIT_USER_NAME'
              value: 'VoiceCode ${config.workerId}'
            }
            {
              name: 'GIT_USER_EMAIL'
              value: '${config.workerId}@voicecode.dev'
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
              value: config.queueName
            }
            {
              name: 'ApplicationInsights__ConnectionString'
              value: appInsightsConnectionString
            }
            {
              name: 'ClaudeCode__MaxConcurrentWorkers'
              value: '3'
            }
            {
              name: 'ClaudeCode__WorkspacePath'
              value: '/workspace'
            }
            {
              name: 'SHALLOW_CLONE'
              value: 'true'
            }
          ]
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 80
              }
              initialDelaySeconds: 60 // Give time for repo clone
              periodSeconds: 10
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 80
              }
              initialDelaySeconds: 90
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1  // Each worker handles one repo
        rules: [
          {
            name: 'queue-based-scaling'
            custom: {
              type: 'azure-servicebus'
              auth: [
                {
                  secretRef: 'servicebus-connection'
                  triggerParameter: 'connection'
                }
              ]
              metadata: {
                queueName: config.queueName
                namespace: serviceBusNamespace
                messageCount: '1'
              }
            }
          }
        ]
        // Idle timeout configuration
        idleTimeout: 300 // 5 minutes default, can be increased to 1800 (30 min)
      }
    }
    workloadProfileName: 'Consumption' // Use consumption plan for cost efficiency
  }
}]

// Output all worker app names and their repos
output workerApps array = [for (config, i) in workerConfigs: {
  name: workerApps[i].name
  workerId: config.workerId
  repo: config.repo
  fqdn: workerApps[i].properties.configuration.ingress.fqdn
}]