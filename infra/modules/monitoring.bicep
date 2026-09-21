targetScope = 'resourceGroup'

@description('Azure region for monitoring resources.')
param location string

@description('Log Analytics workspace name.')
param logAnalyticsWorkspaceName string

@description('Application Insights component name.')
param applicationInsightsName string

@allowed([
  'dev'
  'prod'
])
param environment string

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: environment == 'prod' ? 90 : 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
  tags: {
    'ateaworkplace:component': 'log-analytics'
    'ateaworkplace:environment': environment
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    DisableIpMasking: true
  }
  tags: {
    'ateaworkplace:component': 'application-insights'
    'ateaworkplace:environment': environment
  }
}

output workspaceResourceId string = logAnalytics.id
output workspaceCustomerId string = logAnalytics.properties.customerId
@secure()
output workspaceSharedKey string = logAnalytics.listKeys().primarySharedKey
output applicationInsightsResourceId string = applicationInsights.id
