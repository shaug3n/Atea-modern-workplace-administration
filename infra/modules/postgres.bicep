targetScope = 'resourceGroup'

@description('Azure region for networking and PostgreSQL.')
param location string

@description('Environment name used for tags and HA defaults.')
param environment string

@description('PostgreSQL Flexible Server name.')
param postgresServerName string

@description('Database created for the application.')
param postgresDatabaseName string

@description('PostgreSQL administrator login. The password is passed as a secure deployment parameter.')
param postgresAdminLogin string

@secure()
@description('PostgreSQL administrator password. Never store this in a parameter file.')
param postgresAdminPassword string

@description('PostgreSQL Flexible Server SKU, for example Standard_B1ms.')
param postgresSkuName string

@description('PostgreSQL storage size in GB.')
param postgresStorageSizeGb int

@description('PostgreSQL major version.')
param postgresVersion string

@description('Virtual network name shared by PostgreSQL and Container Apps.')
param virtualNetworkName string

@description('Private DNS zone for PostgreSQL Flexible Server.')
param privateDnsZoneName string = 'privatelink.postgres.database.azure.com'

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: virtualNetworkName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.42.0.0/16'
      ]
    }
  }
  tags: {
    'ateaworkplace:component': 'private-network'
    'ateaworkplace:environment': environment
  }
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-09-01' = {
  parent: virtualNetwork
  name: 'postgres'
  properties: {
    addressPrefix: '10.42.1.0/24'
    delegations: [
      {
        name: 'postgres-flexible-server'
        properties: {
          serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
        }
      }
    ]
  }
}

resource containerAppsSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-09-01' = {
  parent: virtualNetwork
  name: 'container-apps'
  properties: {
    addressPrefix: '10.42.2.0/23'
    serviceEndpoints: [
      { service: 'Microsoft.Storage' }
    ]
    delegations: [
      {
        name: 'container-apps-environment'
        properties: {
          serviceName: 'Microsoft.App/environments'
        }
      }
    ]
  }
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: privateDnsZoneName
  location: 'global'
  tags: {
    'ateaworkplace:component': 'postgres-private-dns'
  }
}

resource privateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: privateDnsZone
  name: '${virtualNetworkName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: location
  sku: {
    name: postgresSkuName
    tier: contains(postgresSkuName, 'B') ? 'Burstable' : 'GeneralPurpose'
  }
  properties: {
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    availabilityZone: '2'
    backup: {
      backupRetentionDays: environment == 'prod' ? 35 : 7
      geoRedundantBackup: environment == 'prod' ? 'Enabled' : 'Disabled'
    }
    highAvailability: {
      mode: environment == 'prod' ? 'ZoneRedundant' : 'Disabled'
      standbyAvailabilityZone: environment == 'prod' ? '1' : null
    }
    network: {
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: privateDnsZone.id
      publicNetworkAccess: 'Disabled'
    }
    storage: {
      autoGrow: 'Enabled'
      storageSizeGB: postgresStorageSizeGb
    }
    version: postgresVersion
  }
  tags: {
    'ateaworkplace:component': 'postgresql'
    'ateaworkplace:environment': environment
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: postgresDatabaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.UTF8'
  }
}

output serverName string = postgres.name
output serverFqdn string = postgres.properties.fullyQualifiedDomainName
output databaseName string = database.name
output containerAppsSubnetId string = containerAppsSubnet.id
