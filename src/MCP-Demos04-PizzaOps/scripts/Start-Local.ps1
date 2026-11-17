[CmdletBinding()]
param(
    [ValidatePattern('^[0-9a-f]{32}$')]
    [string] $RunId = [Guid]::NewGuid().ToString('N'),
    [ValidateRange(10, 300)]
    [int] $ReadinessTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)
{
    throw 'Start-Local.ps1 currently supports Windows only.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$runDirectory = Join-Path $PSScriptRoot ".local-runs\$RunId"
$manifestPath = Join-Path $runDirectory 'run.json'
$manifest = [ordered]@{
    Owner = 'PizzaOps.Start-Local'
    RunId = $RunId
    CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Processes = @()
}

function Save-Manifest
{
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

function Get-FreePort
{
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try
    {
        $listener.Start()
        return ([System.Net.IPEndPoint] $listener.LocalEndpoint).Port
    }
    finally
    {
        $listener.Stop()
    }
}

function Assert-PortAvailable([int] $Port)
{
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try
    {
        $listener.Start()
    }
    catch
    {
        throw "Local port $Port is already in use. Stop that service and retry; no existing process will be reused."
    }
    finally
    {
        $listener.Stop()
    }
}

function Start-OwnedProcess(
    [string] $Name,
    [string] $FilePath,
    [string[]] $ArgumentList,
    [string] $WorkingDirectory,
    [hashtable] $Environment
)
{
    $blockedEnvironment = @(
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
    foreach ($key in $blockedEnvironment)
    {
        if (-not $Environment.ContainsKey($key))
        {
            $Environment[$key] = $null
        }
    }

    $previousEnvironment = @{}
    foreach ($key in $Environment.Keys)
    {
        $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $Environment[$key], 'Process')
    }

    try
    {
        $process = Start-Process `
            -FilePath $FilePath `
            -ArgumentList $ArgumentList `
            -WorkingDirectory $WorkingDirectory `
            -RedirectStandardOutput (Join-Path $runDirectory "$Name.stdout.log") `
            -RedirectStandardError (Join-Path $runDirectory "$Name.stderr.log") `
            -PassThru `
            -WindowStyle Hidden
    }
    finally
    {
        foreach ($key in $Environment.Keys)
        {
            [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process')
        }
    }

    $manifest.Processes += [ordered]@{
        Name = $Name
        ProcessId = $process.Id
        StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('O')
    }
    Save-Manifest
    return $process
}

function Wait-ForTcpPort([System.Diagnostics.Process] $Process, [int] $Port, [int] $TimeoutSeconds)
{
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline)
    {
        if ($Process.HasExited)
        {
            throw "Owned service process $($Process.Id) exited before opening its readiness port."
        }

        $client = [System.Net.Sockets.TcpClient]::new()
        try
        {
            $connect = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
            if ($connect.AsyncWaitHandle.WaitOne(250) -and $client.Connected)
            {
                $client.EndConnect($connect)
                return
            }
        }
        catch
        {
        }
        finally
        {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 200
    }

    throw "Readiness timed out waiting for owned service on port $Port."
}

function Wait-ForHttp(
    [System.Diagnostics.Process] $Process,
    [string] $Url,
    [int] $TimeoutSeconds,
    [int[]] $AcceptableStatusCodes = @(200)
)
{
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    try
    {
        while ([DateTimeOffset]::UtcNow -lt $deadline)
        {
            if ($Process.HasExited)
            {
                throw "Owned service process $($Process.Id) exited before becoming ready."
            }

            try
            {
                $response = $client.GetAsync($Url).GetAwaiter().GetResult()
                try
                {
                    if ([int] $response.StatusCode -in $AcceptableStatusCodes)
                    {
                        return
                    }
                }
                finally
                {
                    $response.Dispose()
                }
            }
            catch
            {
            }

            Start-Sleep -Milliseconds 250
        }
    }
    finally
    {
        $client.Dispose()
    }

    throw "Readiness timed out waiting for HTTP endpoint '$Url'."
}

if (Test-Path -LiteralPath $runDirectory)
{
    throw "Run directory '$RunId' already exists; refusing to reuse it."
}

foreach ($port in @(10000, 10001, 10002))
{
    Assert-PortAvailable $port
}

$azuriteCommand = Get-Command azurite -ErrorAction Stop
$functionsCommand = Get-Command func -ErrorAction Stop
$dotnetCommand = Get-Command dotnet.exe -ErrorAction Stop
$functionsProject = Join-Path $projectRoot 'src\PizzaOps.Functions'
$dashboardProject = Join-Path $projectRoot 'src\PizzaOps.Dashboard\PizzaOps.Dashboard.csproj'

$suffix = $RunId.Substring(0, 10)
$ovensTable = "PizzaOvens$suffix"
$eventsTable = "PizzaEvents$suffix"
$partitionKey = "local-$RunId"
$functionsPort = Get-FreePort
while ($functionsPort -in @(10000, 10001, 10002))
{
    $functionsPort = Get-FreePort
}
$dashboardPort = Get-FreePort
while ($dashboardPort -in @(10000, 10001, 10002, $functionsPort))
{
    $dashboardPort = Get-FreePort
}
$functionsUrl = "http://127.0.0.1:$functionsPort"
$dashboardUrl = "http://127.0.0.1:$dashboardPort"
$mcpEndpoint = "$functionsUrl/runtime/webhooks/mcp"

New-Item -ItemType Directory -Path $runDirectory | Out-Null
Save-Manifest

try
{
    $azuriteScript = $azuriteCommand.Source.Replace("'", "''")
    $debugCommand = "& '$azuriteScript' --inMemoryPersistence --blobHost 127.0.0.1 --queueHost 127.0.0.1 --tableHost 127.0.0.1"
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($debugCommand))
    $powershellExe = Join-Path $PSHOME 'powershell.exe'
    if (-not (Test-Path -LiteralPath $powershellExe))
    {
        $powershellExe = Join-Path $PSHOME 'pwsh.exe'
    }
    $azurite = Start-OwnedProcess `
        -Name 'azurite' `
        -FilePath $powershellExe `
        -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedCommand) `
        -WorkingDirectory $projectRoot `
        -Environment @{}
    Wait-ForTcpPort $azurite 10002 $ReadinessTimeoutSeconds

    $localSettings = @{
        AZURE_FUNCTIONS_ENVIRONMENT = 'Development'
        DOTNET_ENVIRONMENT = 'Development'
        FUNCTIONS_WORKER_RUNTIME = 'dotnet-isolated'
        AzureWebJobsStorage = 'UseDevelopmentStorage=true'
        AzureFunctionsJobHost__logging__logLevel__default = 'Information'
        PizzaOps__TableStorage__Mode = 'Azurite'
        PizzaOps__TableStorage__AzuriteConnectionString = 'UseDevelopmentStorage=true'
        PizzaOps__TableStorage__OvensTableName = $ovensTable
        PizzaOps__TableStorage__EventsTableName = $eventsTable
        PizzaOps__TableStorage__PartitionKey = $partitionKey
    }
    $functionsExecutable = $functionsCommand.Source
    $functionsArguments = @('start', '--port', "$functionsPort")
    if ([IO.Path]::GetExtension($functionsExecutable) -ne '.exe')
    {
        $functionsScript = $functionsExecutable.Replace("'", "''")
        $functionsInvocation = "& '$functionsScript' start --port $functionsPort"
        $functionsEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($functionsInvocation))
        $functionsExecutable = $powershellExe
        $functionsArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', $functionsEncoded)
    }
    $functions = Start-OwnedProcess `
        -Name 'functions' `
        -FilePath $functionsExecutable `
        -ArgumentList $functionsArguments `
        -WorkingDirectory $functionsProject `
        -Environment $localSettings
    Wait-ForTcpPort $functions $functionsPort $ReadinessTimeoutSeconds
    Wait-ForHttp $functions "$mcpEndpoint" $ReadinessTimeoutSeconds @(200, 400, 405)

    $dashboardSettings = @{
        DOTNET_ENVIRONMENT = 'Development'
        ASPNETCORE_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = $dashboardUrl
        PizzaOps__TableStorage__Mode = 'Azurite'
        PizzaOps__TableStorage__AzuriteConnectionString = 'UseDevelopmentStorage=true'
        PizzaOps__TableStorage__OvensTableName = $ovensTable
        PizzaOps__TableStorage__EventsTableName = $eventsTable
        PizzaOps__TableStorage__PartitionKey = $partitionKey
    }
    $dashboard = Start-OwnedProcess `
        -Name 'dashboard' `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @('run', '--project', "`"$dashboardProject`"", '--no-build', '--no-launch-profile') `
        -WorkingDirectory $projectRoot `
        -Environment $dashboardSettings
    Wait-ForHttp $dashboard "$dashboardUrl/health/live" $ReadinessTimeoutSeconds

    Write-Host "Azurite is ready on its isolated in-memory default ports; Functions: $functionsUrl; Dashboard: $dashboardUrl."
    Write-Host "Owned run id: $RunId. Stop it with: .\scripts\Stop-Local.ps1 -RunId $RunId"
    Write-Host 'For a local operator reset, configure only these Development settings in the same PowerShell session:'
    Write-Host "  `$env:PIZZAOPS_DEVELOPMENT_PizzaOps__TableStorage__Mode = 'Azurite'"
    Write-Host "  `$env:PIZZAOPS_DEVELOPMENT_PizzaOps__TableStorage__AzuriteConnectionString = 'UseDevelopmentStorage=true'"
    Write-Host "  `$env:PIZZAOPS_DEVELOPMENT_PizzaOps__TableStorage__OvensTableName = '$ovensTable'"
    Write-Host "  `$env:PIZZAOPS_DEVELOPMENT_PizzaOps__TableStorage__EventsTableName = '$eventsTable'"
    Write-Host "  `$env:PIZZAOPS_DEVELOPMENT_PizzaOps__TableStorage__PartitionKey = '$partitionKey'"
    Write-Host 'Then use PizzaOps.DemoAdmin reset with --environment Development --quiesced and the displayed target scope.'

    [pscustomobject]@{
        RunId = $RunId
        FunctionsUrl = $functionsUrl
        McpEndpoint = $mcpEndpoint
        DashboardUrl = $dashboardUrl
        OvensTableName = $ovensTable
        EventsTableName = $eventsTable
        PartitionKey = $partitionKey
    }
}
catch
{
    if (Test-Path -LiteralPath $manifestPath)
    {
        try
        {
            & (Join-Path $PSScriptRoot 'Stop-Local.ps1') -RunId $RunId
        }
        catch
        {
            Write-Warning 'Owned-run cleanup encountered an error; run Stop-Local.ps1 with the reported run id to retry.'
        }
    }
    throw
}
