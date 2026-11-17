#requires -Version 7.0

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "pizzaops-package-$([Guid]::NewGuid().ToString('N'))"
# Generated projects must not inherit the copied repository's global.json.
$temp = "$testRoot-temp"
$names = @('APPDATA', 'HOME', 'DOTNET_CLI_HOME', 'AZD_CONFIG_DIR', 'TEMP', 'TMP',
    'CustomAfterMicrosoftCommonTargets', 'PIZZAOPS_PACKAGE_EVIDENCE',
    'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'NUGET_PACKAGES', 'MSBUILDDISABLENODEREUSE')
$original = @{}
foreach ($name in $names) { $original[$name] = [Environment]::GetEnvironmentVariable($name) }

try {
    $cache = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the existing NuGet package cache.' }
    $env:NUGET_PACKAGES = ($cache -split ': ', 2)[1].Trim()
    $env:MSBUILDDISABLENODEREUSE = '1'
    $null = New-Item -ItemType Directory -Path $testRoot
    # Copy only source-controlled/nonignored inputs, never the active AZD environment or build outputs.
    $files = & git -C $repositoryRoot ls-files --cached --others --exclude-standard -- `
        azure.yaml global.json infra scripts/azd src/MCP-Demos03-PizzaOps
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate packaging inputs.' }
    foreach ($file in ($files | Sort-Object -Unique)) {
        $destination = Join-Path $testRoot $file
        $null = New-Item -ItemType Directory -Path (Split-Path $destination) -Force
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $file) -Destination $destination
    }

    $profile = Join-Path $testRoot 'profile'
    $evidence = Join-Path $testRoot 'evidence'
    $null = New-Item -ItemType Directory -Path $profile, $temp, $evidence
    $env:APPDATA = if ($IsWindows) { $profile } else { $null }
    $env:HOME = $profile
    $env:DOTNET_CLI_HOME = $profile
    $env:AZD_CONFIG_DIR = Join-Path $profile 'azd'
    $env:TEMP = $temp
    $env:TMP = $temp
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:CustomAfterMicrosoftCommonTargets = Join-Path $PSScriptRoot 'PackageSdkProbe.targets'
    $env:PIZZAOPS_PACKAGE_EVIDENCE = $evidence

    Push-Location (Join-Path $testRoot 'src\MCP-Demos03-PizzaOps')
    try {
        $expectedSdk = (& dotnet --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $expectedSdk -notmatch '^10\.0\.\d+$') {
            throw 'The scoped global.json must resolve a stable .NET 10 SDK.'
        }
    }
    finally { Pop-Location }

    Push-Location $testRoot
    try {
        $rootSdk = (& dotnet --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $rootSdk -cne $expectedSdk) {
            throw "Root SDK $rootSdk does not match the PizzaOps SDK $expectedSdk."
        }
        & azd env new pizzaops-offline --no-prompt
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated local AZD environment.' }

        & dotnet publish .\src\MCP-Demos03-PizzaOps\src\PizzaOps.Functions\PizzaOps.Functions.csproj `
            --configuration Release --artifacts-path (Join-Path $temp 'functions-artifacts') `
            --output (Join-Path $temp 'functions-publish') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Functions publish with external artifacts failed.' }
        Write-Host 'PASS: Functions published with generated extension artifacts outside the repository.'

        & azd package --no-prompt --output-path (Join-Path $testRoot 'artifacts')
        if ($LASTEXITCODE -ne 0) { throw 'Root-invoked AZD packaging failed.' }
    }
    finally { Pop-Location }

    foreach ($service in @('functions', 'dashboard')) {
        $project = if ($service -eq 'functions') { 'PizzaOps.Functions' } else { 'PizzaOps.Dashboard' }
        $actualSdk = (Get-Content -LiteralPath (Join-Path $evidence "$project.sdk") -Raw).Trim()
        if ($actualSdk -cne $expectedSdk) { throw "$project published with SDK $actualSdk instead of $expectedSdk." }
        $packages = @(Get-ChildItem (Join-Path $testRoot 'artifacts') -Filter "*pizzaops-$service-*.zip")
        if ($packages.Count -ne 1) { throw "Expected exactly one $service ZIP." }
        $zip = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
        try {
            $required = @("$project.dll", "$project.runtimeconfig.json")
            if ($service -eq 'functions') { $required += @('host.json', 'worker.config.json', 'extensions.json', '.azurefunctions/function.deps.json') }
            foreach ($entry in $required) {
                if (-not $zip.GetEntry($entry)) { throw "$service package is missing $entry at its root." }
            }
            if ($zip.Entries.FullName -match '(^|/)(local\.settings\.json|\.env)$') {
                throw "$service package includes local configuration."
            }
            $reader = [IO.StreamReader]::new($zip.GetEntry("$project.runtimeconfig.json").Open())
            try { $runtime = $reader.ReadToEnd() | ConvertFrom-Json }
            finally { $reader.Dispose() }
            if ($runtime.runtimeOptions.tfm -cne 'net10.0') { throw "$service package does not target net10.0." }
            if ($service -eq 'functions') {
                $reader = [IO.StreamReader]::new($zip.GetEntry('worker.config.json').Open())
                try { $worker = $reader.ReadToEnd() | ConvertFrom-Json }
                finally { $reader.Dispose() }
                if ($worker.description.language -cne 'dotnet-isolated' -or
                    $worker.description.defaultExecutablePath -cne 'dotnet' -or
                    $worker.description.defaultWorkerPath -cne 'PizzaOps.Functions.dll' -or
                    $worker.description.workerIndexing -ne 'true') {
                    throw 'Functions ZIP does not contain the expected portable worker-indexing configuration.'
                }
            }
        }
        finally { $zip.Dispose() }
        Write-Host "PASS: root AZD packaged $service without --cwd using SDK $actualSdk; ZIP layout and runtime verified."
    }
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $original[$name]) }
    # AZD's update-check subprocess can finish writing after the package command exits.
    foreach ($path in @($testRoot, $temp)) {
        for ($attempt = 0; (Test-Path -LiteralPath $path); $attempt++) {
            try { Remove-Item -LiteralPath $path -Recurse -Force }
            catch [IO.IOException] {
                if ($attempt -ge 4) { throw }
                Start-Sleep -Seconds 1
            }
        }
    }
}
