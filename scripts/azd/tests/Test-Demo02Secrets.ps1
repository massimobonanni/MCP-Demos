#requires -Version 7.0

param(
    [string] $Case
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$hook = Join-Path $PSScriptRoot '..\initialize-demo02-secrets.ps1'
$testKey = 'demo02-offline-test-"quoted"\key'

if ($Case) {
    $env:AZURE_SUBSCRIPTION_ID = '00000000-0000-0000-0000-000000000002'
    $env:resourceGroupName = 'rg-demo02-offline'
    $env:foundryAccountName = 'demo02-offline'
    $env:foundryEndpoint = 'https://demo02-offline.cognitiveservices.azure.com/'

    function az {
        $expected = @(
            'cognitiveservices', 'account', 'keys', 'list',
            '--subscription', $env:AZURE_SUBSCRIPTION_ID,
            '--resource-group', $env:resourceGroupName,
            '--name', $env:foundryAccountName,
            '--query', 'key1', '--output', 'json', '--only-show-errors'
        )
        if (($args -join '|') -cne ($expected -join '|')) {
            throw 'Unexpected Azure CLI arguments.'
        }

        $global:LASTEXITCODE = 0
        switch ($Case) {
            'key-failure' {
                Write-Output $testKey
                Write-Error $testKey -ErrorAction Continue
                $global:LASTEXITCODE = 42
            }
            'malformed-key' { Write-Output "{invalid: $testKey" }
            'empty-key' { Write-Output '""' }
            default { ConvertTo-Json -InputObject $testKey -Compress }
        }
    }

    if ($Case -eq 'import-failure') {
        function dotnet {
            process {
                Write-Output $testKey
                Write-Error $testKey -ErrorAction Continue
                $global:LASTEXITCODE = 43
            }
        }
    }

    if ($Case -eq 'missing-output') {
        Remove-Item Env:\foundryAccountName
    }
    if ($Case -eq 'invalid-endpoint') {
        $env:foundryEndpoint = 'http://demo02-offline.invalid/'
    }

    & $hook
    exit $LASTEXITCODE
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "demo02-secrets-$([Guid]::NewGuid().ToString('N'))"
$originalAppData = $env:APPDATA
$originalHome = $env:HOME
$originalCliHome = $env:DOTNET_CLI_HOME
$originalFirstRun = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$originalTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT

try {
    $null = New-Item -ItemType Directory -Path $testRoot
    $env:APPDATA = if ($IsWindows) { $testRoot } else { $null }
    $env:HOME = $testRoot
    $env:DOTNET_CLI_HOME = $testRoot
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $project = Join-Path $PSScriptRoot '..\..\..\src\MCP-Demos.02-MCPChat\MCP-Demos.02-MCPChat.csproj'
    [xml] $projectXml = Get-Content -LiteralPath $project -Raw
    $secretId = [string] $projectXml.Project.PropertyGroup.UserSecretsId
    $store = if ($IsWindows) {
        Join-Path $testRoot "Microsoft\UserSecrets\$secretId\secrets.json"
    }
    else {
        Join-Path $testRoot ".microsoft\usersecrets\$secretId\secrets.json"
    }

    $cases = @{
        'missing-output' = "Missing AZD value 'foundryAccountName'"
        'invalid-endpoint' = 'absolute HTTPS model endpoint'
        'key-failure' = 'Could not retrieve the Foundry account key'
        'malformed-key' = 'did not return a nonempty Foundry account key'
        'empty-key' = 'did not return a nonempty Foundry account key'
        'import-failure' = 'Could not save Demo 02 user secrets'
    }
    foreach ($testCase in $cases.Keys) {
        $output = & pwsh -NoProfile -File $PSCommandPath -Case $testCase 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($exitCode -eq 0 -or -not $text.Contains($cases[$testCase]) -or $text.Contains($testKey)) {
            throw "Failed offline case '$testCase' (exit $exitCode); output withheld."
        }
        if (Test-Path -LiteralPath $store) {
            throw "Failure case '$testCase' unexpectedly wrote user secrets."
        }
        Write-Host "PASS: $testCase (nonzero exit, safe diagnostic, no secret store write)."
    }

    foreach ($iteration in 1..2) {
        if ($iteration -eq 2) {
            $existing = Get-Content -LiteralPath $store -Raw | ConvertFrom-Json -AsHashtable
            $existing['Unrelated:Setting'] = 'preserve-me'
            $existing['Foundry:ApiKey'] = 'old-offline-test-key'
            $existing | ConvertTo-Json | Set-Content -LiteralPath $store
        }

        $output = & pwsh -NoProfile -File $PSCommandPath -Case success 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($exitCode -ne 0 -or $text.Contains($testKey)) {
            throw "Success case failed (exit $exitCode); output withheld."
        }
        $saved = Get-Content -LiteralPath $store -Raw | ConvertFrom-Json -AsHashtable
        if ($saved['Foundry:Endpoint'] -cne 'https://demo02-offline.cognitiveservices.azure.com/' -or
            $saved['Foundry:ApiKey'] -cne $testKey) {
            throw 'Stored endpoint or key did not match the offline inputs.'
        }
        if ($iteration -eq 2 -and $saved['Unrelated:Setting'] -cne 'preserve-me') {
            throw 'Rerunning the hook did not preserve unrelated user secrets.'
        }
        Write-Host "PASS: real .NET user-secrets import $iteration (isolated profile, exact values, no disclosure)."
    }

    $repositoryRoot = Join-Path $PSScriptRoot '..\..\..'
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'azure.yaml') -Destination $testRoot
    $hookDirectory = Join-Path $testRoot 'scripts\azd'
    $null = New-Item -ItemType Directory -Path $hookDirectory
    Copy-Item -LiteralPath $hook -Destination $hookDirectory
    $output = & azd env new demo02-offline --cwd $testRoot --no-prompt 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the isolated local AZD environment; output withheld.'
    }
    $output = & azd env set `
        'AZURE_SUBSCRIPTION_ID=00000000-0000-0000-0000-000000000002' `
        'resourceGroupName=rg-demo02-offline' `
        'foundryAccountName=demo02-offline' `
        'foundryEndpoint=http://demo02-offline.invalid/' `
        --cwd $testRoot -e demo02-offline --no-prompt 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not configure the isolated local AZD environment; output withheld.'
    }
    $output = & azd hooks run postprovision --cwd $testRoot -e demo02-offline --no-prompt 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join "`n"
    if ($exitCode -eq 0 -or -not $text.Contains('absolute HTTPS model endpoint') -or $text.Contains($testKey)) {
        throw "AZD did not propagate the safe hook failure (exit $exitCode); output withheld."
    }
    Write-Host 'PASS: installed AZD executes postprovision, loads outputs, and propagates hook failure without provisioning.'
}
finally {
    $env:APPDATA = $originalAppData
    $env:HOME = $originalHome
    $env:DOTNET_CLI_HOME = $originalCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $originalFirstRun
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $originalTelemetry
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
