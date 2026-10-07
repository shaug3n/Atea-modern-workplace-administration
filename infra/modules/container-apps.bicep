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

@description('Client ID of the user-assigned workload identity.')
param workloadIdentityClientId string

@description('Key Vault URI that contains the application secrets.')
param keyVaultUri string

@description('Log Analytics workspace resource ID.')
param logAnalyticsWorkspaceResourceId string

@description('Separate Entra app registration client ID selected for this environment.')
param entraClientId string

@description('Separate Entra API audience selected for this environment.')
param entraAudience string

@description('Customer SPA client ID linked to this API registration.')
param customerSpaClientId string

@description('Explicit ingress proxy IP addresses trusted for forwarded client IPs.')
param trustedProxyAddresses string = ''

@description('Exact API consent callback URI selected for this environment.')
param consentRedirectUri string

@description('Exact customer SPA sign-in callback URI.')
param customerRedirectUri string

@description('Exact hosted Atea administrator sign-in callback URI.')
param platformAdminRedirectUri string

@description('Tenant ID that owns the hosted Atea platform operators.')
param platformHomeTenantId string

@description('Entra object IDs allowed to administer the platform.')
param platformAdminObjectIds array

@description('Blob URI for the shared ASP.NET Core Data Protection key ring.')
param dataProtectionBlobUri string

@description('Key Vault key URI used to wrap Data Protection keys.')
param dataProtectionKeyIdentifier string

@description('Exact HTTPS public base URL used by API invite generation.')
param publicBaseUrl string
@description('IPv4 CIDR allowed to access the test deployment from the operator browser.')
param smokeTestSourceCidr string

@description('Whether to expose public ingress. Keep false until the first authenticated smoke test passes.')
param externalIngressEnabled bool = false

@description('Approved HTTPS hostnames. Wildcard hostnames are rejected by the local contract validator.')
param allowedIngressHostnames array

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppEnvironmentName
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

var revisionSuffix = '${substring(toLower(imageTag), 0, 12)}-1'
var platformAdminEnvironmentVariables = [for (objectId, index) in platformAdminObjectIds: {
  name: 'PlatformAuthorization__AdminObjectIds__${index}'
  value: objectId
}]

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
          keyVaultUrl: '${keyVaultUri}secrets/workplace-migration-db'
          name: 'workplace-migration-db'
        }
        {
          identity: workloadIdentityResourceId
          keyVaultUrl: '${keyVaultUri}secrets/consent-signing-key'
          name: 'consent-signing-key'
        }
        {
          identity: workloadIdentityResourceId
          keyVaultUrl: '${keyVaultUri}secrets/continuation-signing-key'
          name: 'continuation-signing-key'
        }
        {
          identity: workloadIdentityResourceId
          keyVaultUrl: '${keyVaultUri}secrets/api-client-secret'
          name: 'api-client-secret'
        }
      ]
      ingress: {
        allowInsecure: false
        external: externalIngressEnabled
        targetPort: 8080
        transport: 'auto'
        ipSecurityRestrictions: [
          {
            name: 'test-operator'
            description: 'Approved operator network for the disposable test deployment.'
            ipAddressRange: smokeTestSourceCidr
            action: 'Allow'
          }
        ]
        customDomains: [for hostname in allowedIngressHostnames: {
          bindingType: 'SniEnabled'
          certificateId: resourceId('Microsoft.App/managedEnvironments/managedCertificates', containerAppEnvironmentName, hostname)
          name: hostname
        }]
        // The first release has no previous revision; ACA still requires weights to total 100.
        // The test deployment remains restricted to the operator CIDR until it is reviewed.
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
                path: '/health/ready'
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
          env: concat([
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: environment == 'prod' ? 'Production' : 'Staging'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'AzureAd__Instance'
              value: az.environment().authentication.loginEndpoint
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
              name: 'AzureAd__ClientSecret'
              secretRef: 'api-client-secret'
            }
            {
              name: 'PlatformAuthorization__HomeTenantId'
              value: platformHomeTenantId
            }
            {
              name: 'HostedAuth__CustomerRedirectUri'
              value: customerRedirectUri
            }
            {
              name: 'HostedAuth__PlatformAdminRedirectUri'
              value: platformAdminRedirectUri
            }
            {
              name: 'DataProtection__BlobUri'
              value: dataProtectionBlobUri
            }
            {
              name: 'DataProtection__KeyIdentifier'
              value: dataProtectionKeyIdentifier
            }
            {
              name: 'DataProtection__ManagedIdentityClientId'
              value: workloadIdentityClientId
            }
            {
              name: 'Onboarding__PublicBaseUrl'
              value: publicBaseUrl
            }
            {
              name: 'Onboarding__CustomerClientId'
              value: customerSpaClientId
            }
            {
              name: 'Onboarding__ApiApplicationIdUri'
              value: entraAudience
            }
            {
              name: 'Onboarding__TrustedProxyAddresses'
              value: trustedProxyAddresses
            }
            {
              name: 'Onboarding__ConsentRedirectUri'
              value: consentRedirectUri
            }
            {
              name: 'ConnectionStrings__WorkplaceDb'
              secretRef: 'workplace-db'
            }
            {
              name: 'Onboarding__ConsentSigningKey'
              secretRef: 'consent-signing-key'
            }
            {
              name: 'Users__ContinuationSigningKey'
              secretRef: 'continuation-signing-key'
            }
          ], platformAdminEnvironmentVariables)
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

resource databaseMigrationJob 'Microsoft.App/jobs@2024-03-01' = {
  name: '${containerAppName}-migration'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workloadIdentityResourceId}': {}
    }
  }
  properties: {
    environmentId: managedEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        { identity: workloadIdentityResourceId, server: registryLoginServer }
      ]
      secrets: [
        {
          identity: workloadIdentityResourceId
          keyVaultUrl: '${keyVaultUri}secrets/workplace-migration-db'
          name: 'workplace-migration-db'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'database-migration'
          image: '${registryLoginServer}/${imageRepository}:${imageTag}'
          command: ['dotnet']
          args: ['Atea.UnifiedWorkplace.Api.dll', '--migrate']
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: environment == 'prod' ? 'Production' : 'Staging' }
            { name: 'ConnectionStrings__WorkplaceMigrationDb', secretRef: 'workplace-migration-db' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
  tags: {
    'ateaworkplace:component': 'database-migration-job'
    'ateaworkplace:environment': environment
  }
}

output managedEnvironmentResourceId string = managedEnvironment.id
output containerAppResourceId string = containerApp.id
output containerAppName string = containerApp.name
output latestRevisionFqdn string = containerApp.properties.latestRevisionFqdn
output migrationJobName string = databaseMigrationJob.name
