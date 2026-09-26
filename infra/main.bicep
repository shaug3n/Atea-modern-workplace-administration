targetScope = 'resourceGroup'

@description('Atea-approved EU/EEA Azure region.')
param location string = 'westeurope'

@allowed(['dev', 'prod'])
param environment string

param containerAppEnvironmentName string
param containerAppsStorageAccountName string
param containerRegistryName string
param keyVaultName string
param logAnalyticsWorkspaceName string
param applicationInsightsName string
param postgresServerName string
param postgresDatabaseName string = 'workplace'
param postgresAdminLogin string = 'workplaceadmin'

@secure()
param postgresAdminPassword string

param postgresSkuName string = 'Standard_B1ms'
param postgresStorageSizeGb int = 32
param postgresVersion string = '16'
param virtualNetworkName string
param workloadIdentityName string = 'workplace-runtime'
@description('Object ID for GitHub Actions OIDC deployment service principal. Required to push application images to ACR.')
param deploymentPrincipalObjectId string = ''
@description('Object ID for the one-time GitHub Actions foundation/bootstrap principal.')
param provisionPrincipalObjectId string = ''

module identity 'modules/identity.bicep' = {
  name: 'workload-identity'
  params: { identityName: workloadIdentityName, location: location }
}

module registry 'modules/container-registry.bicep' = {
  name: 'container-registry'
  params: {
    environment: environment
    location: location
    registryName: containerRegistryName
    workloadIdentityPrincipalId: identity.outputs.principalId
    deploymentPrincipalObjectId: deploymentPrincipalObjectId
    provisionPrincipalObjectId: provisionPrincipalObjectId
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    environment: environment
    keyVaultName: keyVaultName
    location: location
    tenantId: subscription().tenantId
    workloadIdentityPrincipalId: identity.outputs.principalId
    provisionPrincipalObjectId: provisionPrincipalObjectId
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    applicationInsightsName: applicationInsightsName
    environment: environment
    location: location
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'private-postgres'
  params: {
    environment: environment
    location: location
    postgresAdminLogin: postgresAdminLogin
    postgresAdminPassword: postgresAdminPassword
    postgresDatabaseName: postgresDatabaseName
    postgresServerName: postgresServerName
    postgresSkuName: postgresSkuName
    postgresStorageSizeGb: postgresStorageSizeGb
    postgresVersion: postgresVersion
    virtualNetworkName: virtualNetworkName
  }
}

module dataProtection 'modules/data-protection-storage.bicep' = {
  name: 'shared-data-protection-storage'
  params: {
    environment: environment
    location: location
    storageAccountName: containerAppsStorageAccountName
    workloadIdentityPrincipalId: identity.outputs.principalId
    allowedSubnetId: postgres.outputs.containerAppsSubnetId
  }
}

module containerAppsEnvironment 'modules/container-app-environment.bicep' = {
  name: 'container-apps-environment'
  params: {
    containerAppEnvironmentName: containerAppEnvironmentName
    containerAppsSubnetId: postgres.outputs.containerAppsSubnetId
    environment: environment
    location: location
    logAnalyticsCustomerId: monitoring.outputs.workspaceCustomerId
    logAnalyticsSharedKey: monitoring.outputs.workspaceSharedKey
  }
}

output acrLoginServer string = registry.outputs.loginServer
output acrResourceId string = registry.outputs.resourceId
output keyVaultUri string = keyVault.outputs.vaultUri
output keyVaultResourceId string = keyVault.outputs.resourceId
output postgresFqdn string = postgres.outputs.serverFqdn
output postgresDatabaseName string = postgres.outputs.databaseName
output postgresAdminLogin string = postgresAdminLogin
output workloadIdentityResourceId string = identity.outputs.resourceId
output workloadIdentityClientId string = identity.outputs.clientId
output workloadIdentityPrincipalId string = identity.outputs.principalId
output containerAppsEnvironmentResourceId string = containerAppsEnvironment.outputs.resourceId
output containerAppsEnvironmentName string = containerAppsEnvironment.outputs.name
output containerAppsDefaultDomain string = containerAppsEnvironment.outputs.defaultDomain
output containerAppsStaticIp string = containerAppsEnvironment.outputs.staticIp
output containerAppsSubnetId string = postgres.outputs.containerAppsSubnetId
output dataProtectionStorageAccountName string = dataProtection.outputs.storageAccountName
output dataProtectionBlobUri string = dataProtection.outputs.blobUri
output dataProtectionKeyIdentifier string = keyVault.outputs.dataProtectionKeyIdentifier
output logAnalyticsWorkspaceResourceId string = monitoring.outputs.workspaceResourceId
output logAnalyticsWorkspaceCustomerId string = monitoring.outputs.workspaceCustomerId
output applicationInsightsResourceId string = monitoring.outputs.applicationInsightsResourceId
