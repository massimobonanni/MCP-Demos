[CmdletBinding()]
param(
    [ValidateRange(10, 300)]
    [int] $ReadinessTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)
{
    throw 'Test-Local.ps1 currently supports Windows only.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$runId = [Guid]::NewGuid().ToString('N')
$startScript = Join-Path $PSScriptRoot 'Start-Local.ps1'
$stopScript = Join-Path $PSScriptRoot 'Stop-Local.ps1'
$testProject = Join-Path $projectRoot 'tests\PizzaOps.IntegrationTests\PizzaOps.IntegrationTests.csproj'
$environmentNames = @(
    'PIZZAOPS_LOCAL_E2E_TESTS',
    'PIZZAOPS_LOCAL_E2E_ENVIRONMENT',
    'PIZZAOPS_AZURITE_TESTS',
    'PIZZAOPS_AZURITE_CONNECTION_STRING',
    'PIZZAOPS_E2E_FUNCTIONS_LOG_DIRECTORY',
    'PIZZAOPS_MCP_ENDPOINT',
    'PIZZAOPS_DASHBOARD_URL',
    'PIZZAOPS_E2E_OVENS_TABLE',
    'PIZZAOPS_E2E_EVENTS_TABLE',
    'PIZZAOPS_E2E_PARTITION',
    'PLAYWRIGHT_NODEJS_PATH',
    'APPLICATIONINSIGHTS_CONNECTION_STRING',
    'APPINSIGHTS_INSTRUMENTATIONKEY',
    'APPLICATIONINSIGHTS_AUTHENTICATION_STRING',
    'APPLICATIONINSIGHTS_AUTHENTICATION',
    'AzureWebJobsApplicationInsightsConnectionString',
    'OTEL_EXPORTER_OTLP_ENDPOINT',
    'OTEL_EXPORTER_OTLP_HEADERS',
    'OTEL_EXPORTER_OTLP_PROTOCOL',
    'OTEL_EXPORTER_OTLP_CERTIFICATE',
    'OTEL_EXPORTER_OTLP_CLIENT_KEY',
    'OTEL_EXPORTER_OTLP_CLIENT_CERTIFICATE',
    'OTEL_SERVICE_NAME',
    'OTEL_RESOURCE_ATTRIBUTES',
    'OTEL_TRACES_EXPORTER',
    'OTEL_METRICS_EXPORTER',
    'OTEL_LOGS_EXPORTER',
    'AZURE_CLIENT_ID',
    'AZURE_TENANT_ID',
    'AZURE_CLIENT_SECRET',
    'AZURE_FEDERATED_TOKEN_FILE',
    'AZURE_AUTHORITY_HOST',
    'AZURE_SUBSCRIPTION_ID',
    'AzureWebJobsStorage',
    'AZURE_STORAGE_CONNECTION_STRING'
)
$previousEnvironment = @{}
$localRun = $null

foreach ($name in $environmentNames)
{
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

foreach ($name in $environmentNames | Where-Object { $_ -notlike 'PIZZAOPS_*' })
{
    [Environment]::SetEnvironmentVariable($name, $null, 'Process')
}

Push-Location -LiteralPath $projectRoot
try
{
    Write-Host 'Building the integration-test dependency graph before starting isolated local services...'
    & dotnet build $testProject -p:PlaywrightPlatform=win
    if ($LASTEXITCODE -ne 0)
    {
        throw "PizzaOps build failed with exit code $LASTEXITCODE."
    }

    $edgeExecutables = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
    )
    if (-not ($edgeExecutables | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1))
    {
        throw 'Microsoft Edge stable channel is required for the real Blazor DOM E2E test.'
    }

    # Supported Playwright override; never patch files inside the SDK's driver directory.
    [Environment]::SetEnvironmentVariable('PLAYWRIGHT_NODEJS_PATH', (Get-Command node.exe -ErrorAction Stop).Source, 'Process')

    $localRun = & $startScript -RunId $runId -ReadinessTimeoutSeconds $ReadinessTimeoutSeconds
    [Environment]::SetEnvironmentVariable('PIZZAOPS_LOCAL_E2E_TESTS', 'true', 'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_LOCAL_E2E_ENVIRONMENT', 'PizzaOps-Local-E2E', 'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_AZURITE_TESTS', 'true', 'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_AZURITE_CONNECTION_STRING', 'UseDevelopmentStorage=true', 'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_MCP_ENDPOINT', $localRun.McpEndpoint, 'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_DASHBOARD_URL', $localRun.DashboardUrl, 'Process')
    [Environment]::SetEnvironmentVariable(
        'PIZZAOPS_E2E_FUNCTIONS_LOG_DIRECTORY',
        (Join-Path $PSScriptRoot ".local-runs\$runId"),
        'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_E2E_OVENS_TABLE', $localRun.OvensTableName, 'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_E2E_EVENTS_TABLE', $localRun.EventsTableName, 'Process')
    [Environment]::SetEnvironmentVariable('PIZZAOPS_E2E_PARTITION', $localRun.PartitionKey, 'Process')

    Write-Host 'Running the real MCP -> Azurite Table -> Dashboard API integration test...'
    & dotnet test $testProject --no-build --no-restore --filter 'FullyQualifiedName~LocalOrchestrationEndToEndTests|FullyQualifiedName~TableKitchenRepositoryTests' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0)
    {
        throw "PizzaOps local E2E tests failed with exit code $LASTEXITCODE."
    }

    Write-Host 'PizzaOps local E2E tests passed.'
}
finally
{
    foreach ($name in $environmentNames)
    {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }

    try
    {
        if ($null -ne $localRun)
        {
            & $stopScript -RunId $runId
        }
    }
    finally
    {
        Pop-Location
    }
}
