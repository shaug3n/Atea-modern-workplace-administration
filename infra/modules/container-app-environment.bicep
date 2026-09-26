targetScope = 'resourceGroup'

param location string
param environment string
param containerAppEnvironmentName string
param containerAppsSubnetId string
param logAnalyticsCustomerId string
@secure()
param logAnalyticsSharedKey string

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnetId
      internal: false
    }
    zoneRedundant: environment == 'prod'
  }
  tags: {
    'ateaworkplace:component': 'container-apps-environment'
    'ateaworkplace:environment': environment
  }
}

output resourceId string = managedEnvironment.id
output name string = managedEnvironment.name
output defaultDomain string = managedEnvironment.properties.defaultDomain
output staticIp string = managedEnvironment.properties.staticIp
