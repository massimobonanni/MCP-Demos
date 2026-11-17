@description('The AZD environment name.')
@minLength(1)
param environmentName string

@description('Azure region used by PizzaOps resources.')
@minLength(1)
param location string

@description('Hosting profile. The demo profile enables App Service Always On.')
@allowed([
  'dev'
  'demo'
])
param profile string

@description('Flex HTTP always-ready instance count. Leave at zero until the MCP scale group and cost are approved.')
@minValue(0)
@maxValue(1)
param functionsAlwaysReadyHttpInstances int

@description('Optional object ID of an operator authorized to manage the PizzaOps application tables.')
param operatorPrincipalId string

@description('Principal type for the optional table operator.')
@allowed([
  'User'
  'Group'
  'ServicePrincipal'
])
param operatorPrincipalType string

var normalizedEnvironmentName = toLower(environmentName)
var resourceToken = uniqueString(subscription().subscriptionId, resourceGroup().id)
var shortEnvironmentName = take(normalizedEnvironmentName, 12)
var storageAccountName = 'stpizzaops${resourceToken}'
var functionAppName = 'func-pizzaops-mcp-${shortEnvironmentName}-${resourceToken}'
var dashboardAppName = 'app-pizzaops-dashboard-${shortEnvironmentName}-${resourceToken}'
var functionPlanName = 'asp-pizzaops-fn-${take(normalizedEnvironmentName, 5)}-${resourceToken}'
var dashboardPlanName = 'asp-pizzaops-web-${take(normalizedEnvironmentName, 5)}-${resourceToken}'
var applicationInsightsName = 'appi-pizzaops-${shortEnvironmentName}-${resourceToken}'
var logAnalyticsWorkspaceName = 'log-pizzaops-${shortEnvironmentName}-${resourceToken}'
var deploymentContainerName = 'pizzaops-deploy'
var tableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var tableDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
var blobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var queueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var monitoringMetricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'
var resourceTags = {
  'azd-env-name': environmentName
  project: 'mcp-demos'
  demo: '03'
}
var alwaysReady = functionsAlwaysReadyHttpInstances == 0
  ? []
  : [
      {
        name: 'http'
        instanceCount: functionsAlwaysReadyHttpInstances
      }
    ]

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
  tags: resourceTags
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    DisableLocalAuth: true
    WorkspaceResourceId: logAnalytics.id
  }
  tags: resourceTags
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
  resource blobService 'blobServices' = {
    name: 'default'
    resource deploymentContainer 'containers' = {
      name: deploymentContainerName
      properties: {
        publicAccess: 'None'
      }
    }
  }
  resource tableService 'tableServices' = {
    name: 'default'
    resource ovensTable 'tables' = {
      name: 'PizzaOpsOvens'
    }
    resource eventsTable 'tables' = {
      name: 'PizzaOpsEvents'
    }
  }
  tags: resourceTags
}

resource functionPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: functionPlanName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
  tags: resourceTags
}

resource dashboardPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: dashboardPlanName
  location: location
  kind: 'linux'
  sku: profile == 'demo'
    ? {
        name: 'S1'
        tier: 'Standard'
      }
    : {
        name: 'B1'
        tier: 'Basic'
      }
  properties: {
    reserved: true
  }
  tags: resourceTags
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    httpsOnly: true
    serverFarmId: functionPlan.id
    siteConfig: {
      minTlsVersion: '1.2'
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
        alwaysReady: alwaysReady
      }
    }
  }
  tags: resourceTags
}

resource functionAppSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    AzureWebJobsStorage__accountName: storage.name
    AzureWebJobsStorage__credential: 'managedidentity'
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
    APPLICATIONINSIGHTS_AUTHENTICATION_STRING: 'Authorization=AAD'
    WEBSITE_CLOUD_ROLENAME: 'PizzaOps.Functions'
    PizzaOps__TableStorage__Mode: 'ManagedIdentity'
    PizzaOps__TableStorage__TableEndpoint: storage.properties.primaryEndpoints.table
    PizzaOps__TableStorage__OvensTableName: 'PizzaOpsOvens'
    PizzaOps__TableStorage__EventsTableName: 'PizzaOpsEvents'
    PizzaOps__TableStorage__PartitionKey: 'main-kitchen'
  }
}

resource dashboardApp 'Microsoft.Web/sites@2024-04-01' = {
  name: dashboardAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    httpsOnly: true
    serverFarmId: dashboardPlan.id
    siteConfig: {
      alwaysOn: profile == 'demo'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/ready'
      http20Enabled: true
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      webSocketsEnabled: true
    }
  }
  tags: resourceTags
}

resource dashboardAppSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: dashboardApp
  name: 'appsettings'
  properties: {
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
    APPLICATIONINSIGHTS_AUTHENTICATION_STRING: 'Authorization=AAD'
    PizzaOps__TableStorage__Mode: 'ManagedIdentity'
    PizzaOps__TableStorage__TableEndpoint: storage.properties.primaryEndpoints.table
    PizzaOps__TableStorage__OvensTableName: 'PizzaOpsOvens'
    PizzaOps__TableStorage__EventsTableName: 'PizzaOpsEvents'
    PizzaOps__TableStorage__PartitionKey: 'main-kitchen'
  }
}

resource functionsHostBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, blobDataOwnerRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataOwnerRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionsHostQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, queueDataContributorRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', queueDataContributorRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionsHostTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, tableDataContributorRoleId, 'functions-host')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionsDeploymentBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, blobDataContributorRoleId, deploymentContainerName)
  scope: storage::blobService::deploymentContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionsOvensTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, tableDataContributorRoleId, 'ovens')
  scope: storage::tableService::ovensTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionsEventsTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, tableDataContributorRoleId, 'events')
  scope: storage::tableService::eventsTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dashboardOvensTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, dashboardApp.id, tableDataReaderRoleId, 'ovens')
  scope: storage::tableService::ovensTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataReaderRoleId)
    principalId: dashboardApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dashboardEventsTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, dashboardApp.id, tableDataReaderRoleId, 'events')
  scope: storage::tableService::eventsTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataReaderRoleId)
    principalId: dashboardApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource operatorOvensTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(operatorPrincipalId)) {
  name: guid(storage.id, operatorPrincipalId, tableDataContributorRoleId, 'operator-ovens')
  scope: storage::tableService::ovensTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: operatorPrincipalId
    principalType: operatorPrincipalType
  }
}

resource operatorEventsTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(operatorPrincipalId)) {
  name: guid(storage.id, operatorPrincipalId, tableDataContributorRoleId, 'operator-events')
  scope: storage::tableService::eventsTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: operatorPrincipalId
    principalType: operatorPrincipalType
  }
}

resource functionsMonitoringRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, functionApp.id, monitoringMetricsPublisherRoleId)
  scope: applicationInsights
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      monitoringMetricsPublisherRoleId
    )
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dashboardMonitoringRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, dashboardApp.id, monitoringMetricsPublisherRoleId)
  scope: applicationInsights
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      monitoringMetricsPublisherRoleId
    )
    principalId: dashboardApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppName string = functionApp.name
output functionAppId string = functionApp.id
output mcpEndpoint string = 'https://${functionApp.properties.defaultHostName}/runtime/webhooks/mcp'
output dashboardAppName string = dashboardApp.name
output dashboardAppId string = dashboardApp.id
output dashboardUrl string = 'https://${dashboardApp.properties.defaultHostName}'
output storageAccountName string = storage.name
output storageAccountId string = storage.id
output ovensTableName string = storage::tableService::ovensTable.name
output eventsTableName string = storage::tableService::eventsTable.name
output applicationInsightsName string = applicationInsights.name
output logAnalyticsWorkspaceId string = logAnalytics.id
