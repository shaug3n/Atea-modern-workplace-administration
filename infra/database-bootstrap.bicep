targetScope = 'resourceGroup'

param location string
param jobName string
param containerAppsEnvironmentResourceId string
param workloadIdentityResourceId string
param registryLoginServer string
param imageRepository string = 'atea-database-bootstrap'
param imageTag string
param keyVaultUri string
param postgresServerFqdn string
param postgresDatabaseName string
param postgresAdminLogin string

resource bootstrapJob 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workloadIdentityResourceId}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironmentResourceId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        { identity: workloadIdentityResourceId, server: registryLoginServer }
      ]
      secrets: [
        { identity: workloadIdentityResourceId, keyVaultUrl: '${keyVaultUri}secrets/postgres-bootstrap-admin-password', name: 'bootstrap-admin-password' }
        { identity: workloadIdentityResourceId, keyVaultUrl: '${keyVaultUri}secrets/postgres-bootstrap-migration-password', name: 'bootstrap-migration-password' }
        { identity: workloadIdentityResourceId, keyVaultUrl: '${keyVaultUri}secrets/postgres-bootstrap-runtime-password', name: 'bootstrap-runtime-password' }
      ]
    }
    template: {
      containers: [
        {
          name: 'database-bootstrap'
          image: '${registryLoginServer}/${imageRepository}:${imageTag}'
          env: [
            { name: 'PGHOST', value: postgresServerFqdn }
            { name: 'PGPORT', value: '5432' }
            { name: 'PGDATABASE', value: postgresDatabaseName }
            { name: 'PGUSER', value: postgresAdminLogin }
            { name: 'PGSSLMODE', value: 'require' }
            { name: 'PGPASSWORD', secretRef: 'bootstrap-admin-password' }
            { name: 'MIGRATION_PASSWORD', secretRef: 'bootstrap-migration-password' }
            { name: 'RUNTIME_PASSWORD', secretRef: 'bootstrap-runtime-password' }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
  tags: {
    'ateaworkplace:component': 'one-time-database-role-bootstrap'
  }
}

output jobName string = bootstrapJob.name
