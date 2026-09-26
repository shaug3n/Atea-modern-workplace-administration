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
@description('Principal ID of the one-time GitHub Actions provision identity.')
param provisionPrincipalObjectId string = ''

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

resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: 'workplace-data-protection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
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

var keyVaultSecretsOfficerRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
resource provisionSecretsOfficerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(provisionPrincipalObjectId)) {
  name: guid(keyVault.id, provisionPrincipalObjectId, keyVaultSecretsOfficerRoleDefinitionId)
  scope: keyVault
  properties: {
    principalId: provisionPrincipalObjectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsOfficerRoleDefinitionId
  }
}

var keyVaultCryptoUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')

resource cryptoUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, workloadIdentityPrincipalId, keyVaultCryptoUserRoleDefinitionId)
  scope: dataProtectionKey
  properties: {
    principalId: workloadIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultCryptoUserRoleDefinitionId
  }
}

output resourceId string = keyVault.id
output vaultUri string = keyVault.properties.vaultUri
output dataProtectionKeyIdentifier string = '${keyVault.properties.vaultUri}keys/${dataProtectionKey.name}'
