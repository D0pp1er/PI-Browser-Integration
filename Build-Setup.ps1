[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows .NET Framework C# compiler was not found. Run this build on 64-bit Windows with .NET Framework 4.x.'
}

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'dist' }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$work = (New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '.runtime') -Force).FullName
$stage = (New-Item -ItemType Directory -Path (Join-Path $work ('build-' + [guid]::NewGuid().ToString('N')))).FullName

Push-Location $PSScriptRoot
try {
    $app = Join-Path $stage 'Form Assistant.exe'
    & $compiler /nologo /target:winexe /platform:x64 "/out:$app" /reference:System.Windows.Forms.dll Launcher.cs InstallPaths.cs
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }

    $files = @('form-assistant.ts', 'browser-connection.ts', 'browser-page.ts',
        'assistant-prompt.txt', 'local-models.json', 'form.html', 'README.md',
        'THIRD-PARTY-NOTICES.md', 'LICENSE-LFM.txt', 'LICENSE-Pi.txt',
        'LICENSE-llama.cpp.txt', 'LICENSE-agent-browser.txt')
    Copy-Item -LiteralPath $files -Destination $stage
    $payload = Join-Path $stage 'form-assistant.zip'
    $entries = @($app) + @($files | ForEach-Object { Join-Path $stage $_ })
    Compress-Archive -LiteralPath $entries -DestinationPath $payload

    $setup = Join-Path $stage 'FormAssistant-Setup-x64.exe'
    & $compiler /nologo /target:winexe /platform:x64 "/out:$setup" "/resource:$payload,form-assistant.zip" `
        /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll Setup.cs SetupProgress.cs InstallPaths.cs
    if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $installer = Join-Path $output 'FormAssistant-Setup-x64.exe'
    Copy-Item -LiteralPath $setup -Destination $installer -Force
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    Write-Output "Built: $installer"
    Write-Output "SHA-256: $hash"
} finally {
    Pop-Location
    if ((Split-Path -Parent $stage) -eq $work -and (Split-Path -Leaf $stage) -match '^build-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
