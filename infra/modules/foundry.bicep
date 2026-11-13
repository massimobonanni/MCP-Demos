@description('Globally unique name for the Microsoft Foundry account.')
param accountName string

@description('The AZD environment name.')
param environmentName string

@description('Azure region for the Foundry account and its model deployment.')
param location string

@description('Name of the Foundry project.')
param projectName string

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2026-09-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: accountName
    disableLocalAuth: false
    publicNetworkAccess: 'Enabled'
  }
  tags: {
    'azd-env-name': environmentName
    project: 'mcp-demos'
    demo: '02'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2026-09-01' = {
  parent: foundryAccount
  name: projectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    description: 'Microsoft Foundry project for MCP-Demos Demo 02.'
    displayName: 'MCP-Demos Demo 02'
  }
  tags: {
    'azd-env-name': environmentName
    project: 'mcp-demos'
    demo: '02'
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2026-09-01' = {
  parent: foundryAccount
  name: 'gpt-6-luna'
  dependsOn: [
    foundryProject
  ]
  sku: {
    name: 'GlobalStandard'
    capacity: 50
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-6-luna'
      publisher: 'OpenAI'
    }
  }
}

output accountName string = foundryAccount.name
output endpoint string = foundryAccount.properties.endpoint
output projectName string = foundryProject.name
output modelDeploymentName string = modelDeployment.name
