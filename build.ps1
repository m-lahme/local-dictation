param([string]$Output = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
& $dotnet publish (Join-Path $PSScriptRoot 'src\LocalDictation\LocalDictation.csproj') -c Release -r win-x64 --self-contained true -o $Output --packages (Join-Path $PSScriptRoot '.tools\packages')
if($LASTEXITCODE -ne 0){throw 'App build failed'}
& $dotnet publish (Join-Path $PSScriptRoot 'src\NativeWorker\NativeWorker.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $Output 'native') --packages (Join-Path $PSScriptRoot '.tools\packages')
if($LASTEXITCODE -ne 0){throw 'Worker build failed'}
Write-Host "Native app built: $Output\LocalDictation.exe"
