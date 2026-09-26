targetScope = 'resourceGroup'

param location string
param storageAccountName string
param workloadIdentityPrincipalId string
param environment string
param allowedSubnetId string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      virtualNetworkRules: [
        { action: 'Allow', id: allowedSubnetId }
      ]
    }
  }
  tags: {
    'ateaworkplace:component': 'data-protection-key-store'
    'ateaworkplace:environment': environment
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2022-09-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource keyContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-09-01' = {
  parent: blobService
  name: 'dataprotection'
  properties: { publicAccess: 'None' }
}

var blobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
resource keyStoreWriteRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyContainer.id, workloadIdentityPrincipalId, blobDataContributorRoleDefinitionId)
  scope: keyContainer
  properties: {
    principalId: workloadIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: blobDataContributorRoleDefinitionId
  }
}

output storageAccountName string = storageAccount.name
output containerName string = keyContainer.name
output blobUri string = '${storageAccount.properties.primaryEndpoints.blob}${keyContainer.name}/keys.xml'
