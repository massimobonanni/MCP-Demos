#Requires -Version 7.5
[CmdletBinding()]
param(
    [string] $Dll = (Join-Path $PSScriptRoot 'bin\Release\net10.0\StageDirector.dll'),
    [ValidateRange(1024, 65535)] [int] $Port = 5187,
    [switch] $LegacyProtocol,
    [switch] $ObserveBrowser
)

$ErrorActionPreference = 'Stop'
$Dll = (Resolve-Path -LiteralPath $Dll).Path
$url = "http://127.0.0.1:$Port"
$scenes = @('welcome', 'focus', 'energy', 'celebration')
$titles = @('WELCOME', 'FOCUS MODE', 'ENERGY MODE', 'THANK YOU!')

function Assert($Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

function Start-Stage([string] $PortValue) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.ArgumentList.Add($Dll)
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $null = $start.Environment.Remove('STAGE_DIRECTOR_PORT')
    if ($PortValue) { $start.Environment['STAGE_DIRECTOR_PORT'] = $PortValue }
    [Diagnostics.Process]::Start($start)
}

function Wait-StageExit($Process) {
    Assert ($Process.WaitForExit(10000)) 'Stage Director did not exit within 10 seconds.'
}

function Test-StartupFailure([string] $PortValue, [string] $Diagnostic) {
    $child = Start-Stage $PortValue
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        Wait-StageExit $child
        Assert ($child.ExitCode -eq 1 -and $stdout.Result -ceq '' -and $stderr.Result -match $Diagnostic) `
            "Expected startup failure ($Diagnostic), exit $($child.ExitCode); stderr: $($stderr.Result)"
    } finally {
        if (!$child.HasExited) { Stop-Process -Id $child.Id }
        $child.Dispose()
    }
}

function Send-Rpc($Id, [string] $Method, $Params) {
    if (!$LegacyProtocol) {
        $Params['_meta'] = @{
            'io.modelcontextprotocol/protocolVersion' = '2026-07-28'
            'io.modelcontextprotocol/clientInfo' = @{ name = 'stage-smoke'; version = '1.0' }
            'io.modelcontextprotocol/clientCapabilities' = @{}
        }
    }
    $message = @{ jsonrpc = '2.0'; method = $Method; params = $Params }
    if ($null -ne $Id) { $message.id = $Id }
    $server.StandardInput.WriteLine(($message | ConvertTo-Json -Compress -Depth 20))
    $server.StandardInput.Flush()
}

function Receive-Rpc {
    # Parsing every stdout line also detects banners, diagnostics, and stray blank lines.
    $line = $server.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
    Assert ($line -and $line.StartsWith('{')) "Expected MCP JSON on stdout; got: $line"
    $message = ConvertFrom-Json -InputObject $line -AsHashtable -DateKind String
    Assert ($message.jsonrpc -ceq '2.0' -and $message.Contains('id') -and !$message.Contains('error') -and
        $message.Contains('result')) "Unexpected MCP response: $line"
    $message
}

function Invoke-Rpc([string] $Method, $Params) {
    $id = ++$script:nextId
    Send-Rpc $id $Method $Params
    $reply = Receive-Rpc
    Assert ($reply.id -eq $id) "Expected response $id, got $($reply.id)."
    $reply.result
}

function Invoke-Tool([string] $Name, $Arguments = @{}) {
    Invoke-Rpc 'tools/call' @{ name = $Name; arguments = $Arguments }
}

function Get-Http([string] $Path, [string] $Method = 'GET') {
    Invoke-WebRequest -Uri "$url$Path" -Method $Method -SkipHttpErrorCheck -NoProxy -TimeoutSec 5 `
        -Headers @{ Origin = 'http://example.invalid' }
}

function Assert-EqualSnapshot($Expected, $Actual) {
    Assert (($Expected | ConvertTo-Json -Depth 20 -Compress) -ceq ($Actual | ConvertTo-Json -Depth 20 -Compress)) `
        'Stage snapshots differ.'
}

function Get-Stage {
    $result = Invoke-Tool 'get_stage_status'
    Assert (!$result.isError -and $result.structuredContent) 'Missing structured stage snapshot.'
    $response = Get-Http '/api/stage'
    Assert ($response.StatusCode -eq 200) 'The stage API is unavailable.'
    Assert-EqualSnapshot $result.structuredContent ($response.Content | ConvertFrom-Json -AsHashtable -DateKind String)
    $result.structuredContent
}

function Assert-Change($Result, $Before, [string] $Scene, [bool] $Changed) {
    $expectedRevision = $Before.revision + [int] $Changed
    $change = $Result.structuredContent
    Assert (!$Result.isError -and $change.success -eq $true -and $change.changed -eq $Changed -and
        $change.previousScene -ceq $Before.scene -and $change.currentScene -ceq $Scene -and
        $change.revision -eq $expectedRevision -and $change.stage.revision -eq $expectedRevision -and
        $change.stage.scene -ceq $Scene -and $change.stage.title -ceq $titles[$scenes.IndexOf($Scene)]) `
        "Unexpected transition to $Scene at revision $expectedRevision."
    if (!$Changed) { Assert-EqualSnapshot $Before $change.stage }
    Assert-EqualSnapshot $change.stage (Get-Stage)
    $change.stage
}

Test-StartupFailure 'not-a-port' 'STAGE_DIRECTOR_PORT.*1024.*65535'
Test-StartupFailure '1023' 'STAGE_DIRECTOR_PORT.*1024.*65535'
Test-StartupFailure '65536' 'STAGE_DIRECTOR_PORT.*1024.*65535'

# A socket is enough to simulate a port conflict; no second Stage Director is started.
$occupied = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$occupied.ExclusiveAddressUse = $true
$occupied.Start()
try {
    $occupiedPort = $occupied.LocalEndpoint.Port
    Test-StartupFailure "$occupiedPort" "Cannot start the Stage Director dashboard on http://127\.0\.0\.1:$occupiedPort/"
} finally {
    $occupied.Stop()
}
Write-Host 'PASS: invalid and occupied ports fail on stderr only.'

$nextId = 0
$server = Start-Stage $(if ($Port -ne 5187) { "$Port" } else { '' })
try {
    $stderr = $server.StandardError.ReadToEndAsync()
    if ($LegacyProtocol) {
        $init = Invoke-Rpc 'initialize' @{
            protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'stage-smoke'; version = '1.0' }
        }
        Assert ($init.protocolVersion -ceq '2025-11-25' -and $init.capabilities.Contains('tools')) 'MCP initialization failed.'
        Send-Rpc $null 'notifications/initialized' @{}
    } else {
        $discovery = Invoke-Rpc 'server/discover' @{}
        Assert ('2026-07-28' -cin $discovery.supportedVersions -and
            $discovery.capabilities.Contains('tools')) "Unexpected MCP discovery: $($discovery | ConvertTo-Json -Compress -Depth 10)"
    }
    $catalog = (Invoke-Rpc 'tools/list' @{}).tools
    Assert ((($catalog.name | Sort-Object) -join ',') -ceq 'activate_scene,get_stage_status,reset_stage') 'Unexpected MCP catalog.'
    foreach ($tool in $catalog) {
        $schema = $tool.inputSchema
        Assert ($tool.description -and $schema.type -ceq 'object' -and $schema.additionalProperties -eq $false) "Invalid contract for $($tool.name)."
        if ($tool.name -ceq 'activate_scene') {
            Assert ($schema.properties.Count -eq 1 -and $schema.properties.scene.type -ceq 'string' -and
                ($schema.properties.scene.enum -join ',') -ceq ($scenes -join ',') -and
                ($schema.required -join ',') -ceq 'scene') 'Invalid activation schema.'
        } else {
            Assert ($schema.properties.Count -eq 0 -and !$schema.required) "Unexpected arguments for $($tool.name)."
        }
    }
    $initial = Get-Stage
    Assert ($initial.scene -ceq 'welcome' -and $initial.revision -eq 1 -and $initial.title -ceq 'WELCOME' -and
        $initial.effect -ceq 'ambient' -and $initial.theme.background -ceq '#101B39' -and
        ($initial.availableScenes -join ',') -ceq ($scenes -join ',') -and
        [DateTimeOffset]::Parse($initial.activatedAtUtc).Offset -eq [TimeSpan]::Zero) 'Invalid initial state.'

    $bindings = @(Get-NetTCPConnection -LocalPort $Port -State Listen)
    Assert ($bindings.Count -eq 1 -and $bindings[0].LocalAddress -ceq '127.0.0.1') 'Dashboard is not bound exclusively to IPv4 loopback.'
    foreach ($asset in @{ '/' = 'text/html'; '/styles.css' = 'text/css'; '/stage.js' = 'text/javascript'; '/api/stage' = 'application/json' }.GetEnumerator()) {
        $http = Get-Http $asset.Key
        Assert ($http.StatusCode -eq 200 -and "$($http.Headers['Content-Type'])" -like "$($asset.Value)*" -and
            "$($http.Headers['Cache-Control'])" -ceq 'no-store' -and "$($http.Headers['X-Content-Type-Options'])" -ceq 'nosniff' -and
            "$($http.Headers['Content-Security-Policy'])" -match "script-src 'self'" -and
            "$($http.Headers['Content-Security-Policy'])" -notmatch 'unsafe-inline|unsafe-eval' -and
            !$http.Headers.ContainsKey('Access-Control-Allow-Origin')) "Bad GET response for $($asset.Key)."
        foreach ($method in 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE', 'OPTIONS') {
            $http = Get-Http $asset.Key $method
            Assert ($http.StatusCode -in 404, 405) "Unexpected success for $method $($asset.Key)."
        }
    }
    foreach ($path in '/api', '/api/activate', '/api/reset', '/README.md', '/web/', '/API/STAGE') {
        Assert ((Get-Http $path).StatusCode -eq 404) "Unexpected route: $path."
    }
    Assert-EqualSnapshot $initial ((Get-Http '/api/stage?scene=energy').Content | ConvertFrom-Json -AsHashtable -DateKind String)
    Assert-EqualSnapshot $initial (Get-Stage)
    Write-Host "PASS: exact tool catalog, initial state, HTTP allowlist and headers, read-only state, and loopback TCP bind at $url."

    if ($ObserveBrowser) {
        Write-Host "Open $url now. The narratives start in 30 seconds."
        Start-Sleep -Seconds 30
    }
    Write-Host "Opening: We're starting a technical talk. We need fewer distractions."
    $state = Get-Stage
    $state = Assert-Change (Invoke-Tool 'activate_scene' @{ scene = 'focus' }) $state 'focus' $true
    if ($ObserveBrowser) { Start-Sleep -Seconds 8 }
    Write-Host 'Change of pace: The audience needs some energy!'
    $state = Assert-Change (Invoke-Tool 'activate_scene' @{ scene = 'energy' }) $state 'energy' $true
    if ($ObserveBrowser) { Start-Sleep -Seconds 8 }
    Write-Host "Finale: We're done. Let's have a grand finale!"
    $state = Assert-Change (Invoke-Tool 'activate_scene' @{ scene = 'celebration' }) $state 'celebration' $true
    $state = Assert-Change (Invoke-Tool 'activate_scene' @{ scene = 'celebration' }) $state 'celebration' $false
    if ($ObserveBrowser) { Start-Sleep -Seconds 8 }

    foreach ($invalid in @(@{ scene = 'laser_show' }, @{ scene = 'FOCUS' }, @{ scene = 1 }, @{}, @{ scene = 'focus'; extra = $true })) {
        $result = Invoke-Tool 'activate_scene' $invalid
        Assert ($result.isError -eq $true -and ($result.content.text -join ' ') -match 'INVALID_SCENE.*welcome, focus, energy, celebration') 'Invalid scene was not rejected cleanly.'
        Assert-EqualSnapshot $state (Get-Stage)
    }
    $state = Assert-Change (Invoke-Tool 'reset_stage') $state 'welcome' $true
    $state = Assert-Change (Invoke-Tool 'reset_stage') $state 'welcome' $false

    # Pipeline requests; responses may arrive out of order. Revision order must describe an atomic history.
    $requests = @{}
    1..36 | ForEach-Object {
        $id = ++$nextId
        $scene = $scenes[$_ % $scenes.Count]
        $requests[[string] $id] = $scene
        Send-Rpc $id 'tools/call' @{ name = 'activate_scene'; arguments = @{ scene = $scene } }
    }
    $changed = @(1..36 | ForEach-Object {
        $reply = Receive-Rpc
        $change = $reply.result.structuredContent
        $id = [string] $reply.id
        Assert (!$reply.result.isError -and $change.success -and $requests.ContainsKey($id) -and
            $change.currentScene -ceq $requests[$id] -and $change.stage.scene -ceq $change.currentScene -and
            $change.stage.revision -eq $change.revision -and
            $change.stage.title -ceq $titles[$scenes.IndexOf($change.currentScene)]) 'Inconsistent pipelined result.'
        $requests.Remove($id)
        if ($change.changed) { $change }
        else { Assert ($change.previousScene -ceq $change.currentScene) 'An unchanged call reported a transition.' }
    } | Sort-Object revision)
    Assert ($changed.Count -gt 1) 'No meaningful changes in the pipelined calls.'
    foreach ($change in $changed) {
        Assert ($change.revision -eq ($state.revision + 1) -and $change.previousScene -ceq $state.scene -and
            $change.currentScene -cne $state.scene) 'Revisions are not a contiguous history of effective changes.'
        $state = $change.stage
    }
    Assert-EqualSnapshot $state (Get-Stage)
    Assert (((Invoke-Rpc 'tools/list' @{}).tools | ConvertTo-Json -Depth 20 -Compress) -ceq
        ($catalog | ConvertTo-Json -Depth 20 -Compress)) 'Tool catalog changed after calls.'
    Write-Host 'PASS: all narratives, invalid inputs, idempotency, and pipelined revisions.'

    $server.StandardInput.Close()
    $tail = $server.StandardOutput.ReadToEndAsync()
    Wait-StageExit $server
    Assert ($server.ExitCode -eq 0 -and $tail.Result -ceq '' -and $stderr.Result -match 'dashboard stopped') `
        "Expected clean EOF shutdown and no stray stdout; stderr: $($stderr.Result)"
    $released = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    $released.ExclusiveAddressUse = $true
    try { $released.Start() } finally { $released.Stop() }
    Write-Host 'PASS: stdout purity, EOF shutdown, and immediate port reuse.'
} finally {
    if (!$server.HasExited) {
        $server.StandardInput.Close()
        if (!$server.WaitForExit(3000)) { Stop-Process -Id $server.Id }
    }
    $server.Dispose()
}
