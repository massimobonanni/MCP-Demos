using './main.bicep'

param environmentName = readEnvironmentVariable('AZURE_ENV_NAME')
param subscriptionId = readEnvironmentVariable('AZURE_SUBSCRIPTION_ID')
param location = readEnvironmentVariable('AZURE_LOCATION')
param pizzaOpsLocation = readEnvironmentVariable('AZURE_LOCATION', '')
param pizzaOpsProfile = readEnvironmentVariable('PIZZAOPS_PROFILE', 'dev')
param pizzaOpsFunctionsAlwaysReadyHttpInstances = int(readEnvironmentVariable(
  'PIZZAOPS_FUNCTIONS_ALWAYS_READY_HTTP_INSTANCES',
  '0'
))
param pizzaOpsOperatorPrincipalId = readEnvironmentVariable('PIZZAOPS_OPERATOR_PRINCIPAL_ID', '')
param pizzaOpsOperatorPrincipalType = readEnvironmentVariable('PIZZAOPS_OPERATOR_PRINCIPAL_TYPE', 'User')
