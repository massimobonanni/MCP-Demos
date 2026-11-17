targetScope = 'subscription'

@description('The AZD environment name.')
@minLength(1)
@maxLength(77)
param environmentName string

@description('The Azure subscription ID selected for this AZD environment.')
@minLength(1)
param subscriptionId string

@description('Azure region for the Foundry account and model deployment.')
@allowed([
  'australiaeast'
  'brazilsouth'
  'canadacentral'
  'canadaeast'
  'centralus'
  'eastus'
  'eastus2'
  'francecentral'
  'germanywestcentral'
  'italynorth'
  'japaneast'
  'koreacentral'
  'northcentralus'
  'norwayeast'
  'polandcentral'
  'southafricanorth'
  'southcentralus'
  'southindia'
  'southeastasia'
  'spaincentral'
  'swedencentral'
  'switzerlandnorth'
  'switzerlandwest'
  'uaenorth'
  'uksouth'
  'westeurope'
  'westus'
  'westus3'
])
param location string

@description('Optional Azure region for PizzaOps. Defaults to the AZURE_LOCATION region.')
param pizzaOpsLocation string = ''

@description('PizzaOps hosting profile. The demo profile enables App Service Always On.')
@allowed([
  'dev'
  'demo'
])
param pizzaOpsProfile string = 'dev'

@description('Number of Flex HTTP always-ready instances. Keep at zero until the MCP scale group and cost are approved.')
@minValue(0)
@maxValue(1)
param pizzaOpsFunctionsAlwaysReadyHttpInstances int = 0

@description('Optional operator object ID to authorize reset access to the two PizzaOps tables.')
param pizzaOpsOperatorPrincipalId string = ''

@description('Principal type for the optional PizzaOps table operator.')
@allowed([
  'User'
  'Group'
  'ServicePrincipal'
])
param pizzaOpsOperatorPrincipalType string = 'User'

var normalizedEnvironmentName = toLower(environmentName)
var resourceGroupName = 'rg-mcp-demos-${normalizedEnvironmentName}'
var foundryAccountName = 'aif-${uniqueString(subscriptionId, resourceGroupName)}'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
  tags: {
    'azd-env-name': environmentName
    project: 'mcp-demos'
    demo: '02'
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry-${uniqueString(resourceGroup.id)}'
  scope: resourceGroup
  params: {
    accountName: foundryAccountName
    environmentName: environmentName
    location: location
    projectName: 'demo02'
  }
}

module pizzaops 'modules/pizzaops.bicep' = {
  name: 'pizzaops-${uniqueString(resourceGroup.id)}'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: empty(pizzaOpsLocation) ? location : pizzaOpsLocation
    profile: pizzaOpsProfile
    functionsAlwaysReadyHttpInstances: pizzaOpsFunctionsAlwaysReadyHttpInstances
    operatorPrincipalId: pizzaOpsOperatorPrincipalId
    operatorPrincipalType: pizzaOpsOperatorPrincipalType
  }
}

output resourceGroupName string = resourceGroup.name
output foundryAccountName string = foundry.outputs.accountName
output foundryEndpoint string = foundry.outputs.endpoint
output foundryProjectName string = foundry.outputs.projectName
output modelDeploymentName string = foundry.outputs.modelDeploymentName
output pizzaOpsFunctionAppName string = pizzaops.outputs.functionAppName
output pizzaOpsFunctionAppId string = pizzaops.outputs.functionAppId
output pizzaOpsMcpEndpoint string = pizzaops.outputs.mcpEndpoint
output pizzaOpsDashboardAppName string = pizzaops.outputs.dashboardAppName
output pizzaOpsDashboardAppId string = pizzaops.outputs.dashboardAppId
output pizzaOpsDashboardUrl string = pizzaops.outputs.dashboardUrl
output pizzaOpsStorageAccountName string = pizzaops.outputs.storageAccountName
output pizzaOpsStorageAccountId string = pizzaops.outputs.storageAccountId
output pizzaOpsOvensTableName string = pizzaops.outputs.ovensTableName
output pizzaOpsEventsTableName string = pizzaops.outputs.eventsTableName
output pizzaOpsApplicationInsightsName string = pizzaops.outputs.applicationInsightsName
output pizzaOpsLogAnalyticsWorkspaceId string = pizzaops.outputs.logAnalyticsWorkspaceId
