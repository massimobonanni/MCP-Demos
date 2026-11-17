using './main.bicep'

param environmentName = readEnvironmentVariable('AZURE_ENV_NAME')
param subscriptionId = readEnvironmentVariable('AZURE_SUBSCRIPTION_ID')
param location = readEnvironmentVariable('AZURE_LOCATION')
param pizzaOpsOperatorPrincipalId = readEnvironmentVariable('PIZZAOPS_OPERATOR_PRINCIPAL_ID', '')
param pizzaOpsOperatorPrincipalType = readEnvironmentVariable('PIZZAOPS_OPERATOR_PRINCIPAL_TYPE', 'User')
