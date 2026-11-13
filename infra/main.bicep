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

output resourceGroupName string = resourceGroup.name
output foundryAccountName string = foundry.outputs.accountName
output foundryEndpoint string = foundry.outputs.endpoint
output foundryProjectName string = foundry.outputs.projectName
output modelDeploymentName string = foundry.outputs.modelDeploymentName
