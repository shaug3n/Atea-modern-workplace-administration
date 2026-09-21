targetScope = 'resourceGroup'

@description('Azure region for Container Apps.')
param location string

@description('Environment name used for names, tags and replica bounds.')
param environment string

@description('Container Apps managed environment name.')
param containerAppEnvironmentName string

@description('Combined API and SPA Container App name.')
param containerAppName string

@description('Container registry login server.')
param registryLoginServer string

@description('Container image repository name.')
param imageRepository string

@description('Immutable image tag, normally the Git commit SHA.')
param imageTag string

@description('User-assigned identity resource ID used for ACR and Key Vault.')
param workloadIdentityResourceId string

@description('Key Vault URI that contains the application secrets.')
param keyVaultUri string

@description('Private subnet for the Container Apps environment.')
param containerAppsSubnetId string

@description('Log Analytics workspace customer ID.')
param logAnalyticsCustomerId string

@description('Log Analytics workspace resource ID.')
param logAnalyticsWorkspaceResourceId string

@secure()
@description('Log Analytics shared key used by Container Apps diagnostics.')
param logAnalyticsSharedKey string

@description('Separate Entra app registration client ID selected for this environment.')
param entraClientId string

@description('Separate Entra API audience selected for this environment.')
param entraAudience string

@description('One exact Entra redirect URI selected for this environment.')
param entraRedirectUri string

@description('Approved HTTPS hostnames. Wildcard hostnames are rejected by the local contract validator.')
param allowedIngressHostnames array

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppEnvironmentName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    infrastructureSubnetId: containerAppsSubnetId
    zoneRedundant: environment == 'prod'
  }
  tags: {
    'ateaworkplace:component': 'container-apps-environment'
    'ateaworkplace:environment': environment
  }
}

resource managedCertificates 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' = [for hostname in allowedIngressHostnames: {
  parent: managedEnvironment
  name: hostname
  location: location
  properties: {
    domainControlValidation: 'CNAME'
    subjectName: hostname
  }
}]

var revisionSuffix = replace(replace(toLower(imageTag), '_', '-'), '.', '-')

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workloadIdentityResourceId}': {}
    }
  }
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Multiple'
      registries: [
        {
          identity: workloadIdentityResourceId
          server: registryLoginServer
        }
      ]
      secrets: [
        {
          identity: workloadIdentityResourceId
          keyVaultUrl: '${keyVaultUri}secrets/workplace-db'
          name: 'workplace-db'
        }
        {
          identity: workloadIdentityResourceId
          keyVaultUrl: '${keyVaultUri}secrets/consent-signing-key'
          name: 'consent-signing-key'
        }
      ]
      ingress: {
        allowInsecure: false
        external: true
        targetPort: 8080
        transport: 'auto'
        customDomains: [for hostname in allowedIngressHostnames: {
          bindingType: 'SniEnabled'
          certificateId: resourceId('Microsoft.App/managedEnvironments/managedCertificates', containerAppEnvironmentName, hostname)
          name: hostname
        }]
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'workplace'
          image: '${registryLoginServer}/${imageRepository}:${imageTag}'
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 6
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 30
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ]
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: environment == 'prod' ? 'Production' : 'Development'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'AzureAd__Instance'
              value: 'https://login.microsoftonline.com/'
            }
            {
              name: 'AzureAd__TenantId'
              value: 'organizations'
            }
            {
              name: 'AzureAd__ClientId'
              value: entraClientId
            }
            {
              name: 'AzureAd__Audience'
              value: entraAudience
            }
            {
              name: 'Onboarding__PublicBaseUrl'
              value: 'https://${first(allowedIngressHostnames)}'
            }
            {
              name: 'Onboarding__ConsentRedirectUri'
              value: entraRedirectUri
            }
            {
              name: 'ConnectionStrings__WorkplaceDb'
              secretRef: 'workplace-db'
            }
            {
              name: 'Onboarding__ConsentSigningKey'
              secretRef: 'consent-signing-key'
            }
          ]
        }
      ]
      revisionSuffix: revisionSuffix
      scale: {
        minReplicas: environment == 'prod' ? 2 : 1
        maxReplicas: environment == 'prod' ? 10 : 3
      }
    }
  }
  dependsOn: managedCertificates
  tags: {
    'ateaworkplace:component': 'combined-api-spa'
    'ateaworkplace:environment': environment
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'container-apps'
  scope: managedEnvironment
  properties: {
    workspaceId: logAnalyticsWorkspaceResourceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output managedEnvironmentResourceId string = managedEnvironment.id
output containerAppResourceId string = containerApp.id
output containerAppName string = containerApp.name
output latestRevisionFqdn string = containerApp.properties.latestRevisionFqdn
