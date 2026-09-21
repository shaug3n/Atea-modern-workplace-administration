targetScope = 'resourceGroup'

@description('Atea-approved single Azure region. The development default stays in the EU/EEA.')
param location string = 'westeurope'

@allowed([
  'dev'
  'prod'
])
@description('Deployment environment.')
param environment string

@description('Immutable container image tag, normally the Git commit SHA.')
param imageTag string

@description('Container image repository in ACR.')
param imageRepository string = 'atea-unified-workplace'

@description('Globally unique Azure Container Registry name.')
param containerRegistryName string

@description('Container Apps managed environment name.')
param containerAppEnvironmentName string

@description('Combined API and SPA Container App name.')
param containerAppName string

@description('User-assigned workload identity name.')
param workloadIdentityName string

@description('Globally unique Key Vault name.')
param keyVaultName string

@description('Log Analytics workspace name used by Container Apps and Application Insights.')
param logAnalyticsWorkspaceName string

@description('Application Insights component name.')
param applicationInsightsName string

@description('PostgreSQL Flexible Server name.')
param postgresServerName string

@description('PostgreSQL database name.')
param postgresDatabaseName string = 'workplace'

@description('PostgreSQL administrator login.')
param postgresAdminLogin string = 'workplaceadmin'

@secure()
@description('PostgreSQL administrator password. Supply from a secret store at deployment time; never commit it to a parameter file.')
param postgresAdminPassword string

@description('PostgreSQL SKU.')
param postgresSkuName string

@description('PostgreSQL storage size in GB.')
param postgresStorageSizeGb int

@description('PostgreSQL major version.')
param postgresVersion string = '16'

@description('Virtual network name shared by private PostgreSQL and Container Apps.')
param virtualNetworkName string

@description('Approved exact hostnames for HTTPS ingress. Wildcard hostnames are not allowed.')
param allowedIngressHostnames array

@description('Development Entra app client ID. Keep separate from production.')
param entraDevelopmentClientId string

@description('Development Entra API audience.')
param entraDevelopmentAudience string

@description('Development Entra redirect URI allowlist.')
param entraDevelopmentRedirectUris array

@description('Production Entra app client ID. Keep separate from development.')
param entraProductionClientId string

@description('Production Entra API audience.')
param entraProductionAudience string

@description('Production Entra redirect URI allowlist.')
param entraProductionRedirectUris array

var selectedEntraClientId = environment == 'prod' ? entraProductionClientId : entraDevelopmentClientId
var selectedEntraAudience = environment == 'prod' ? entraProductionAudience : entraDevelopmentAudience
var selectedEntraRedirectUri = first(environment == 'prod' ? entraProductionRedirectUris : entraDevelopmentRedirectUris)

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    identityName: workloadIdentityName
    location: location
  }
}

module containerregistry 'modules/container-registry.bicep' = {
  name: 'containerregistry'
  params: {
    environment: environment
    location: location
    registryName: containerRegistryName
    workloadIdentityPrincipalId: identity.outputs.principalId
  }
}

module keyvault 'modules/key-vault.bicep' = {
  name: 'keyvault'
  params: {
    environment: environment
    keyVaultName: keyVaultName
    location: location
    tenantId: subscription().tenantId
    workloadIdentityPrincipalId: identity.outputs.principalId
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
  name: 'postgres'
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

module containerapps 'modules/container-apps.bicep' = {
  name: 'containerapps'
  params: {
    allowedIngressHostnames: allowedIngressHostnames
    containerAppEnvironmentName: containerAppEnvironmentName
    containerAppName: containerAppName
    containerAppsSubnetId: postgres.outputs.containerAppsSubnetId
    entraAudience: selectedEntraAudience
    entraClientId: selectedEntraClientId
    entraRedirectUri: selectedEntraRedirectUri
    environment: environment
    imageRepository: imageRepository
    imageTag: imageTag
    keyVaultUri: keyvault.outputs.vaultUri
    location: location
    logAnalyticsCustomerId: monitoring.outputs.workspaceCustomerId
    logAnalyticsWorkspaceResourceId: monitoring.outputs.workspaceResourceId
    logAnalyticsSharedKey: monitoring.outputs.workspaceSharedKey
    registryLoginServer: containerregistry.outputs.loginServer
    workloadIdentityResourceId: identity.outputs.resourceId
  }
}

output containerAppResourceId string = containerapps.outputs.containerAppResourceId
output containerAppName string = containerapps.outputs.containerAppName
output containerAppHost string = containerapps.outputs.latestRevisionFqdn
output keyVaultResourceId string = keyvault.outputs.resourceId
output postgresServerFqdn string = postgres.outputs.serverFqdn
