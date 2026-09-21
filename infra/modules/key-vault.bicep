targetScope = 'resourceGroup'

@description('Azure region for Key Vault.')
param location string

@description('Globally unique Key Vault name.')
param keyVaultName string

@allowed([
  'dev'
  'prod'
])
param environment string

@description('Tenant ID used by Key Vault RBAC.')
param tenantId string

@description('Principal ID of the Container Apps workload identity.')
param workloadIdentityPrincipalId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: environment == 'prod' ? 90 : 30
    enablePurgeProtection: environment == 'prod'
    publicNetworkAccess: 'Enabled'
  }
  tags: {
    'ateaworkplace:component': 'key-vault'
    'ateaworkplace:environment': environment
  }
}

var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, workloadIdentityPrincipalId, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    principalId: workloadIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
  }
}

output resourceId string = keyVault.id
output vaultUri string = keyVault.properties.vaultUri
