#requires -Version 7.0

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$failureMessage = 'Could not validate the local prerequisites.'
$apiKey = $null
$keyOutput = $null
$secretsJson = $null

try {
    if (-not [string]::IsNullOrEmpty($env:DEMO02_SKIP_LOCAL_SECRETS)) {
        $failureMessage = 'DEMO02_SKIP_LOCAL_SECRETS must be true and is allowed only in GitHub Actions (GITHUB_ACTIONS=true). Unset it for local provisioning.'
        if ($env:DEMO02_SKIP_LOCAL_SECRETS -cne 'true' -or $env:GITHUB_ACTIONS -cne 'true') {
            throw $failureMessage
        }

        Write-Host 'Skipping Demo 02 local user-secret materialization explicitly in GitHub Actions; no key retrieved.'
        return
    }

    foreach ($command in @('az', 'dotnet')) {
        $failureMessage = "Required command '$command' was not found. Install Azure CLI and the .NET 10 SDK and ensure both are on PATH."
        $null = Get-Command $command -ErrorAction Stop
    }

    foreach ($name in @('AZURE_SUBSCRIPTION_ID', 'resourceGroupName', 'foundryAccountName', 'foundryEndpoint')) {
        $failureMessage = "Missing AZD value '$name'. Run provisioning successfully, or refresh the intended environment with 'azd env refresh -e <environment>' before retrying the hook."
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            throw $failureMessage
        }
    }

    $failureMessage = 'The foundryEndpoint output must be an absolute HTTPS model endpoint. Check the Bicep output and refresh the intended AZD environment.'
    $endpoint = $null
    if (-not [Uri]::TryCreate($env:foundryEndpoint, [UriKind]::Absolute, [ref]$endpoint) -or
        $endpoint.Scheme -ne 'https' -or -not [string]::IsNullOrEmpty($endpoint.UserInfo)) {
        throw $failureMessage
    }

    $project = Join-Path $PSScriptRoot '..\..\src\MCP-Demos.02-MCPChat\MCP-Demos.02-MCPChat.csproj'
    $failureMessage = 'The Demo 02 project was not found relative to the hook. Restore the repository layout before retrying.'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw $failureMessage
    }

    $failureMessage = 'Could not retrieve the Foundry account key. Run az login for the target tenant and check the AZD subscription/resource outputs and Microsoft.CognitiveServices/accounts/listKeys/action permission on that account.'
    $keyOutput = & az cognitiveservices account keys list `
        --subscription $env:AZURE_SUBSCRIPTION_ID `
        --resource-group $env:resourceGroupName `
        --name $env:foundryAccountName `
        --query key1 --output json --only-show-errors 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw $failureMessage
    }

    $failureMessage = 'Azure CLI did not return a nonempty Foundry account key. Check that key-based authentication is enabled and retry the hook.'
    $apiKey = ($keyOutput -join "`n") | ConvertFrom-Json
    if ($apiKey -isnot [string] -or [string]::IsNullOrWhiteSpace($apiKey)) {
        throw $failureMessage
    }

    $failureMessage = 'Could not save Demo 02 user secrets. Check the .NET 10 SDK, the project UserSecretsId, and write access to the current user secrets store, then retry the hook.'
    $secretsJson = @{
        'Foundry:Endpoint' = $env:foundryEndpoint
        'Foundry:ApiKey' = $apiKey
    } | ConvertTo-Json -Compress

    # Import via stdin so the key is never a command-line argument or temporary file.
    $secretsJson | & dotnet user-secrets set --project $project 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw $failureMessage
    }

    Write-Host 'Demo 02 Foundry endpoint and API key saved in the current user .NET secrets store.'
}
catch {
    # Native output and parsing exceptions can contain the key; report only safe stage context.
    [Console]::Error.WriteLine("Demo 02 user secrets setup failed: $failureMessage CLI output was suppressed to protect credentials.")
    exit 1
}
finally {
    $apiKey = $null
    $keyOutput = $null
    $secretsJson = $null
}
