#requires -Version 7.0

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$names = @('AZURE_ENV_NAME', 'AZURE_SUBSCRIPTION_ID', 'AZURE_LOCATION',
    'PIZZAOPS_OPERATOR_PRINCIPAL_ID', 'PIZZAOPS_OPERATOR_PRINCIPAL_TYPE')
$original = @{}
foreach ($name in $names) { $original[$name] = [Environment]::GetEnvironmentVariable($name) }

try {
    $env:AZURE_ENV_NAME = 'pizzaops-offline'
    $env:AZURE_SUBSCRIPTION_ID = '00000000-0000-0000-0000-000000000003'
    $env:AZURE_LOCATION = 'eastus2'
    $env:PIZZAOPS_PROFILE = 'dev'
    $env:PIZZAOPS_FUNCTIONS_ALWAYS_READY_HTTP_INSTANCES = '0'
    $env:PIZZAOPS_OPERATOR_PRINCIPAL_ID = ''
    $env:PIZZAOPS_OPERATOR_PRINCIPAL_TYPE = 'User'

    $json = & az bicep build --file (Join-Path $repositoryRoot 'infra\main.bicep') --stdout
    if ($LASTEXITCODE -ne 0) { throw 'Bicep compilation failed.' }
    $template = $json | ConvertFrom-Json -Depth 100
    $module = @($template.resources | Where-Object { $_.name -like "*pizzaops-*" })
    if ($module.Count -ne 1) { throw 'Expected one PizzaOps module.' }
    if ($module[0].properties.template.variables.resourceTags.demo -cne '04') {
        throw 'PizzaOps resources must use the Demo 04 metadata tag.'
    }
    $sites = @($module[0].properties.template.resources | Where-Object { $_.type -eq 'Microsoft.Web/sites' })
    $functionApp = @($sites | Where-Object { $_.kind -eq 'functionapp,linux' })
    if ($functionApp.Count -ne 1 -or
        $functionApp[0].properties.functionAppConfig.runtime.name -cne 'dotnet-isolated' -or
        $functionApp[0].properties.functionAppConfig.runtime.version -cne '10') {
        throw 'Flex Consumption must configure the .NET 10 isolated runtime through functionAppConfig.runtime.'
    }
    $functionSettings = @($module[0].properties.template.resources | Where-Object {
        $_.type -eq 'Microsoft.Web/sites/config' -and
        $_.name -ceq "[format('{0}/{1}', variables('functionAppName'), 'appsettings')]"
    })
    if ($functionSettings.Count -ne 1) { throw 'Expected one Function App settings resource.' }
    foreach ($setting in @('FUNCTIONS_WORKER_RUNTIME', 'FUNCTIONS_WORKER_RUNTIME_VERSION', 'FUNCTIONS_EXTENSION_VERSION')) {
        if ($functionSettings[0].properties.PSObject.Properties.Name -contains $setting) {
            throw "Flex Consumption must not define the legacy app setting $setting."
        }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot 'azure.yaml') -Raw
    foreach ($service in @('pizzaops-functions', 'pizzaops-dashboard')) {
        $site = @($sites | Where-Object {
            $_.tags -ceq "[union(variables('resourceTags'), createObject('azd-service-name', '$service'))]"
        })
        if ($site.Count -ne 1 -or $manifest -cnotmatch "(?m)^  ${service}:\r?$") {
            throw "AZD service discovery is not unique/aligned for $service."
        }
    }
    $groups = @($template.resources | Where-Object { $_.type -eq 'Microsoft.Resources/resourceGroups' })
    if ($groups.Count -ne 1 -or $groups[0].tags.'azd-env-name' -cne "[parameters('environmentName')]") {
        throw 'AZD must discover exactly one shared resource group through its environment tag.'
    }
    foreach ($name in @('resourceGroupName', 'foundryAccountName', 'foundryEndpoint',
        'pizzaOpsFunctionAppName', 'pizzaOpsDashboardAppName', 'pizzaOpsDashboardUrl', 'pizzaOpsMcpEndpoint')) {
        if (-not $template.outputs.$name) { throw "Missing nonsecret output contract: $name" }
    }

    foreach ($profile in @('dev', 'demo')) {
        $env:PIZZAOPS_PROFILE = $profile
        $json = & az bicep build-params --file (Join-Path $repositoryRoot 'infra\main.bicepparam') --stdout
        if ($LASTEXITCODE -ne 0) { throw "Bicep parameters failed to compile for $profile." }
        $compiled = $json | ConvertFrom-Json -Depth 100
        $parameters = ($compiled.parametersJson | ConvertFrom-Json).parameters
        if ($parameters.location.value -cne 'eastus2' -or
            $parameters.pizzaOpsLocation.value -cne $parameters.location.value -or
            $parameters.pizzaOpsProfile.value -cne $profile) {
            throw "Shared AZURE_LOCATION/profile did not reach the $profile parameters."
        }
    }
    Write-Host 'PASS: Bicep and dev/demo parameters compile; AZD discovery/output contracts and shared region match.'
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $original[$name]) }
}
