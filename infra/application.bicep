targetScope = 'resourceGroup'

@allowed(['dev', 'prod'])
param environment string
param location string
param containerAppEnvironmentName string
param containerAppName string
param registryLoginServer string
param imageRepository string = 'atea-unified-workplace'
param imageTag string
param workloadIdentityResourceId string
param workloadIdentityClientId string
param keyVaultUri string
param logAnalyticsWorkspaceResourceId string
param entraApiClientId string
param entraApiAudience string
param consentRedirectUri string
param customerRedirectUri string
param platformAdminRedirectUri string
param platformHomeTenantId string
param platformAdminObjectIds array
param dataProtectionBlobUri string
param dataProtectionKeyIdentifier string
param publicBaseUrl string
@description('IPv4 CIDR allowed to access the test deployment from the operator browser.')
param smokeTestSourceCidr string
param allowedIngressHostnames array = []
param externalIngressEnabled bool = false

module application 'modules/container-apps.bicep' = {
  name: 'application-release'
  params: {
    allowedIngressHostnames: allowedIngressHostnames
    containerAppEnvironmentName: containerAppEnvironmentName
    containerAppName: containerAppName
    consentRedirectUri: consentRedirectUri
    customerRedirectUri: customerRedirectUri
    dataProtectionBlobUri: dataProtectionBlobUri
    dataProtectionKeyIdentifier: dataProtectionKeyIdentifier
    entraAudience: entraApiAudience
    entraClientId: entraApiClientId
    environment: environment
    externalIngressEnabled: externalIngressEnabled
    imageRepository: imageRepository
    imageTag: imageTag
    keyVaultUri: keyVaultUri
    location: location
    logAnalyticsWorkspaceResourceId: logAnalyticsWorkspaceResourceId
    platformAdminObjectIds: platformAdminObjectIds
    platformAdminRedirectUri: platformAdminRedirectUri
    platformHomeTenantId: platformHomeTenantId
    publicBaseUrl: publicBaseUrl
    smokeTestSourceCidr: smokeTestSourceCidr
    registryLoginServer: registryLoginServer
    workloadIdentityClientId: workloadIdentityClientId
    workloadIdentityResourceId: workloadIdentityResourceId
  }
}

output containerAppResourceId string = application.outputs.containerAppResourceId
output containerAppName string = application.outputs.containerAppName
output containerAppHost string = application.outputs.latestRevisionFqdn
output migrationJobName string = application.outputs.migrationJobName
