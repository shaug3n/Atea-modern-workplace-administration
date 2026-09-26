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

@description('Principal ID of the GitHub Actions deployment identity.')
param deploymentPrincipalObjectId string = ''
@description('Principal ID of the one-time GitHub Actions provision identity.')
param provisionPrincipalObjectId string = ''

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
      quarantinePolicy: {
        status: 'enabled'
      }
      retentionPolicy: {
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

var acrPushRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')

resource acrPushRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deploymentPrincipalObjectId)) {
  name: guid(registry.id, deploymentPrincipalObjectId, 'acrpush')
  scope: registry
  properties: {
    principalId: deploymentPrincipalObjectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPushRoleDefinitionId
  }
}

resource provisionAcrPushRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(provisionPrincipalObjectId)) {
  name: guid(registry.id, provisionPrincipalObjectId, 'acrpush-provision')
  scope: registry
  properties: {
    principalId: provisionPrincipalObjectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPushRoleDefinitionId
  }
}

output resourceId string = registry.id
output loginServer string = registry.properties.loginServer
