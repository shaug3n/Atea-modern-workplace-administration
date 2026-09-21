targetScope = 'resourceGroup'

@description('Azure region for the user-assigned workload identity.')
param location string

@description('Stable name for the identity used by Container Apps to access ACR and Key Vault.')
param identityName string

resource workloadIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: {
    'ateaworkplace:component': 'workload-identity'
  }
}

output resourceId string = workloadIdentity.id
output principalId string = workloadIdentity.properties.principalId
output clientId string = workloadIdentity.properties.clientId
