targetScope = 'resourceGroup'

@description('Azure region for the registry.')
param location string

@description('Globally unique Azure Container Registry name.')
param registryName string

@allowed([
  'dev'
  'prod'
])
param environment string

@description('Principal ID of the Container Apps workload identity.')
param workloadIdentityPrincipalId string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: environment == 'prod' ? 'Premium' : 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    policies: {
      quarantine: {
        status: 'enabled'
      }
      retention: {
        days: environment == 'prod' ? 30 : 7
        status: 'enabled'
      }
      trustPolicy: {
        status: 'enabled'
      }
    }
  }
  tags: {
    'ateaworkplace:component': 'container-registry'
    'ateaworkplace:environment': environment
  }
}

var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, workloadIdentityPrincipalId, 'acrpull')
  scope: registry
  properties: {
    principalId: workloadIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

output resourceId string = registry.id
output loginServer string = registry.properties.loginServer
