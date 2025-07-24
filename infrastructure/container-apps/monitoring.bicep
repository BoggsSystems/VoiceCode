@description('Application Insights name')
param appInsightsName string

@description('Action Group email')
param alertEmail string = 'alerts@voicecode.dev'

@description('Container App names to monitor')
param containerAppNames array = [
  'voicecode-orchestrator'
  'voicecode-worker'
]

// Existing Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

// Action Group for alerts
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'voicecode-alerts'
  location: 'global'
  properties: {
    groupShortName: 'VCAlertsGrp'
    enabled: true
    emailReceivers: [
      {
        name: 'email-alert'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// Metric Alerts for Container Apps
resource cpuAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = [for appName in containerAppNames: {
  name: '${appName}-high-cpu'
  location: 'global'
  properties: {
    description: 'Alert when CPU usage is over 80%'
    severity: 2
    enabled: true
    scopes: [
      resourceId('Microsoft.App/containerApps', appName)
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'CPU Usage'
          metricName: 'CpuPercentage'
          metricNamespace: 'Microsoft.App/containerApps'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    targetResourceType: 'Microsoft.App/containerApps'
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}]

resource memoryAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = [for appName in containerAppNames: {
  name: '${appName}-high-memory'
  location: 'global'
  properties: {
    description: 'Alert when memory usage is over 85%'
    severity: 2
    enabled: true
    scopes: [
      resourceId('Microsoft.App/containerApps', appName)
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Memory Usage'
          metricName: 'MemoryPercentage'
          metricNamespace: 'Microsoft.App/containerApps'
          operator: 'GreaterThan'
          threshold: 85
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    targetResourceType: 'Microsoft.App/containerApps'
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}]

// Worker-specific alerts
resource workerScalingAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'voicecode-worker-scaling'
  location: 'global'
  properties: {
    description: 'Alert when worker replicas exceed 8'
    severity: 3
    enabled: true
    scopes: [
      resourceId('Microsoft.App/containerApps', 'voicecode-worker')
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Replica Count'
          metricName: 'ReplicaCount'
          metricNamespace: 'Microsoft.App/containerApps'
          operator: 'GreaterThan'
          threshold: 8
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    targetResourceType: 'Microsoft.App/containerApps'
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// Application Insights alerts
resource failureRateAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'voicecode-high-failure-rate'
  location: 'global'
  properties: {
    description: 'Alert when request failure rate exceeds 5%'
    severity: 1
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Failed Requests'
          metricName: 'requests/failed'
          metricNamespace: 'Microsoft.Insights/components'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    targetResourceType: 'Microsoft.Insights/components'
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

resource responseTimeAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'voicecode-slow-response'
  location: 'global'
  properties: {
    description: 'Alert when average response time exceeds 5 seconds'
    severity: 2
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Response Time'
          metricName: 'requests/duration'
          metricNamespace: 'Microsoft.Insights/components'
          operator: 'GreaterThan'
          threshold: 5000 // milliseconds
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    targetResourceType: 'Microsoft.Insights/components'
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// Log Analytics queries for monitoring
resource workerErrorQuery 'Microsoft.OperationalInsights/savedSearches@2020-08-01' = {
  name: 'WorkerErrors'
  location: resourceGroup().location
  properties: {
    category: 'VoiceCode'
    displayName: 'Worker Service Errors'
    query: '''
      ContainerAppConsoleLogs_CL
      | where ContainerAppName_s == "voicecode-worker"
      | where Log_s contains "ERROR" or Log_s contains "Exception"
      | project TimeGenerated, ContainerName_s, Log_s
      | order by TimeGenerated desc
    '''
    functionAlias: 'WorkerErrors'
    version: 2
  }
}

resource scalingEventsQuery 'Microsoft.OperationalInsights/savedSearches@2020-08-01' = {
  name: 'ScalingEvents'
  location: resourceGroup().location
  properties: {
    category: 'VoiceCode'
    displayName: 'Container Apps Scaling Events'
    query: '''
      ContainerAppSystemLogs_CL
      | where EventCategory_s == "Scaling"
      | project TimeGenerated, ContainerAppName_s, Reason_s, ReplicaCount_d
      | order by TimeGenerated desc
    '''
    functionAlias: 'ScalingEvents'
    version: 2
  }
}

// Dashboard for monitoring
resource monitoringDashboard 'Microsoft.Portal/dashboards@2020-09-01-preview' = {
  name: 'voicecode-container-apps-dashboard'
  location: resourceGroup().location
  properties: {
    lenses: [
      {
        order: 0
        parts: [
          {
            position: {
              x: 0
              y: 0
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  content: '# VoiceCode Container Apps Monitoring\n\n## Key Metrics\n- Worker Scaling Status\n- CPU/Memory Usage\n- Request Performance\n- Error Rates'
                }
              }
            }
          }
        ]
      }
    ]
  }
}

output actionGroupId string = actionGroup.id
output dashboardId string = monitoringDashboard.id