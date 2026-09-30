param([switch]$Visual)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
Push-Location $root
try {
    & $dotnet build tests/NativeAudit/NativeAudit.csproj -c Release --packages .tools/packages -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    $audit = Join-Path $root 'tests/NativeAudit/bin/Release/net10.0-windows10.0.19041.0/NativeAudit.dll'
    $data = Join-Path $root ('work/tests/' + [Guid]::NewGuid().ToString('N'))
    foreach ($mode in @('--headless', '--hotkey-input-test')) {
        & $dotnet $audit $mode $data
        if ($LASTEXITCODE -ne 0) { throw "Regression failed: $mode" }
    }
    if ($Visual) {
        & $dotnet $audit --visual $data
        if ($LASTEXITCODE -ne 0) { throw 'Visual render failed' }
    }
} finally {
    Pop-Location
}
