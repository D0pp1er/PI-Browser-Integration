param(
    [string]$BrowserPath,
    [string]$ModelEndpoint = 'http://127.0.0.1:9931/v1',
    [string]$ModelId = 'LiquidAI/LFM3-1.7B-GGUF:Q4_K_M',
    [switch]$Headed,
    [switch]$BrowserOnly
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '.runtime'
$env:PI_CODING_AGENT_DIR = Join-Path $runtime 'pi'
$env:PI_TELEMETRY = '0'
$env:AGENT_BROWSER_SOCKET_DIR = Join-Path $runtime 'sockets'
$env:AGENT_BROWSER_HOME = Join-Path $runtime 'browser-home'
$env:PI_FORM_BROWSER = (Get-ChildItem (Join-Path $runtime 'node_modules') -Filter 'agent-browser*.exe' -Recurse | Select-Object -First 1).FullName
$env:PI_FORM_SESSION = 'pi-form-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
New-Item -ItemType Directory -Force $env:PI_CODING_AGENT_DIR, $env:AGENT_BROWSER_SOCKET_DIR | Out-Null

function Invoke-Browser {
    & $env:PI_FORM_BROWSER --session $env:PI_FORM_SESSION @args
    if ($LASTEXITCODE -ne 0) { throw "agent-browser failed with exit code $LASTEXITCODE." }
}

if (-not $BrowserPath) {
    $BrowserPath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
}
if (-not (Test-Path -LiteralPath $BrowserPath)) { throw "Browser executable not found at $BrowserPath." }

if (-not $BrowserOnly) {
    $uri = [Uri]$ModelEndpoint
    if (-not $uri.IsLoopback) { throw 'Use a local model endpoint for this test.' }
    $models = Invoke-RestMethod "$($ModelEndpoint.TrimEnd('/'))/models" -TimeoutSec 5
    if ($ModelId -notin $models.data.id) { throw "The endpoint does not list $ModelId." }
    @{
        providers = @{
            'local-lfm' = @{
                baseUrl = $ModelEndpoint
                api = 'openai-completions'
                apiKey = 'local'
                compat = @{ supportsReasoningEffort = $false; supportsDeveloperRole = $false; supportsStore = $false; thinkingFormat = 'qwen-chat-template' }
                models = @(@{ id = $ModelId; reasoning = $true; contextWindow = 8192; maxTokens = 1024 })
            }
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding ascii (Join-Path $env:PI_CODING_AGENT_DIR 'models.json')
}

$name = 'Mira Test'
$email = 'mira@example.test'
$visit = 'Ticket office'
$notes = 'Please send the opening hours.'
$expected = "Dummy receipt: $name | $email | $visit | $notes | yes"
$formUri = [Uri](Join-Path $PSScriptRoot 'form.html')
$profile = Join-Path $runtime $env:PI_FORM_SESSION
$debugFile = Join-Path $profile 'DevToolsActivePort'
$launchArgs = @('--remote-debugging-port=0', "--user-data-dir=`"$profile`"", '--no-first-run', 'about:blank')
$windowStyle = 'Normal'
if (-not $Headed) {
    $launchArgs += @('--headless=new', '--disable-gpu')
    $windowStyle = 'Hidden'
}

Push-Location $PSScriptRoot
$browserProcess = $null
try {
    $browserProcess = Start-Process -FilePath $BrowserPath -ArgumentList $launchArgs -WindowStyle $windowStyle -PassThru
    $debugInfo = @()
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        if ($browserProcess.HasExited) { throw "Browser exited with code $($browserProcess.ExitCode)." }
        if (Test-Path -LiteralPath $debugFile) {
            $debugInfo = @(Get-Content -LiteralPath $debugFile)
            if ($debugInfo.Count -ge 2) { break }
        }
        Start-Sleep -Milliseconds 200
    }
    if ($debugInfo.Count -lt 2) { throw 'Browser did not publish its local DevTools endpoint.' }
    $cdp = "ws://127.0.0.1:$($debugInfo[0])$($debugInfo[1])"
    Invoke-Browser --cdp $cdp open $formUri.AbsoluteUri
    Invoke-Browser snapshot -i
    if ($BrowserOnly) {
        Invoke-Browser fill '#name' $name
        Invoke-Browser fill '#email' $email
        Invoke-Browser select '#visit' $visit
        Invoke-Browser fill '#notes' $notes
        Invoke-Browser check '[name="dummy"]'
        Invoke-Browser click '#appointment button[type="submit"]'
    } else {
        $prompt = "Use the form tools. Fill name as $name, email as $email, visit as $visit, notes as $notes Set dummy to yes. Submit the local dummy form and report the receipt."
        & pi --offline --no-session --no-builtin-tools --no-extensions --no-mcp --no-context-files --no-skills -e './browser-tools.ts' --model "local-lfm/$ModelId" --thinking off --system-prompt "You help fill a local dummy form. Use the form tools. Report the exact receipt after submission." --mode json -p $prompt |
            Tee-Object -FilePath (Join-Path $runtime 'pi-test.jsonl') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Pi failed with exit code $LASTEXITCODE." }
    }
    $actual = (Invoke-Browser get text '#receipt' | Out-String).Trim()
    if ($actual -cne $expected) { throw "Receipt mismatch. Expected [$expected], got [$actual]." }
    Write-Output "PASS $actual"
} finally {
    & $env:PI_FORM_BROWSER --session $env:PI_FORM_SESSION close 2>$null | Out-Null
    if ($browserProcess -and -not $browserProcess.HasExited) { Stop-Process -Id $browserProcess.Id }
    Pop-Location
}




