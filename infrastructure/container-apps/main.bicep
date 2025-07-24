@description('The location for all resources')
param location string = resourceGroup().location

@description('Environment name prefix')
param environmentName string = 'voicecode'

@description('Container Registry name')
param containerRegistryName string = 'voicecodebuildsprod'

@description('Key Vault name')
param keyVaultName string = 'voicecodedevkveus'

@description('Service Bus namespace name')
param serviceBusNamespace string = 'voicecodeservicebus'

@description('Storage account name')
param storageAccountName string = 'voicecodestorage'

@description('Application Insights name')
param appInsightsName string = 'voicecode-insights'

@description('Log Analytics workspace name')
param logAnalyticsName string = '${environmentName}-logs'

// Existing resources
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: containerRegistryName
}

resource serviceBusNamespaceResource 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespace
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

// Create Log Analytics workspace if it doesn't exist
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Container Apps Environment
resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2023-11-02-preview' = {
  name: '${environmentName}-env'
  location: location
  properties: {
    daprAIInstrumentationKey: appInsights.properties.InstrumentationKey
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
      {
        name: 'Spot-D4'
        workloadProfileType: 'D4'
        minimumCount: 0
        maximumCount: 10
      }
    ]
  }
}

// Storage for persistent workspaces
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
  }
}

resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  name: '${storageAccount.name}/default/workspaces'
  properties: {
    shareQuota: 100
    accessTier: 'TransactionOptimized'
  }
}

// Managed Identity for Container Apps
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${environmentName}-identity'
  location: location
}

// Grant Key Vault access to managed identity
resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  name: '${keyVault.name}/add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: managedIdentity.properties.principalId
        permissions: {
          secrets: ['get', 'list']
        }
      }
    ]
  }
}

// Grant ACR pull access to managed identity
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, managedIdentity.id, 'acrpull')
  scope: containerRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d') // AcrPull
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Service Bus Data Receiver role for managed identity
resource serviceBusDataReceiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespaceResource.id, managedIdentity.id, 'ServiceBusDataReceiver')
  scope: serviceBusNamespaceResource
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4fc7a66e-8e0e-4e5e-8f4c-0c3f8c3b9a9e')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Service Bus Data Sender role for managed identity
resource serviceBusDataSenderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespaceResource.id, managedIdentity.id, 'ServiceBusDataSender')
  scope: serviceBusNamespaceResource
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Dapr Components
resource daprPubSub 'Microsoft.App/managedEnvironments/daprComponents@2023-11-02-preview' = {
  name: 'pubsub'
  parent: containerAppEnvironment
  properties: {
    componentType: 'pubsub.azure.servicebus'
    version: 'v1'
    metadata: [
      {
        name: 'namespaceName'
        value: '${serviceBusNamespace}.servicebus.windows.net'
      }
      {
        name: 'azureClientId'
        value: managedIdentity.properties.clientId
      }
    ]
    scopes: ['orchestrator', 'worker', 'dispatcher']
  }
}

resource daprStateStore 'Microsoft.App/managedEnvironments/daprComponents@2023-11-02-preview' = {
  name: 'statestore'
  parent: containerAppEnvironment
  properties: {
    componentType: 'state.azure.blobstorage'
    version: 'v1'
    metadata: [
      {
        name: 'accountName'
        value: storageAccountName
      }
      {
        name: 'containerName'
        value: 'state'
      }
      {
        name: 'azureClientId'
        value: managedIdentity.properties.clientId
      }
    ]
    scopes: ['orchestrator', 'worker']
  }
}

// Output values for Container App deployments
output environmentId string = containerAppEnvironment.id
output managedIdentityId string = managedIdentity.id
output managedIdentityClientId string = managedIdentity.properties.clientId
output logAnalyticsWorkspaceId string = logAnalytics.id
output containerRegistryLoginServer string = containerRegistry.properties.loginServer