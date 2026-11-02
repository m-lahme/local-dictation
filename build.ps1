#Requires -Version 5.1
param([string]$Output = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
& $dotnet publish (Join-Path $PSScriptRoot 'src\LocalDictation\LocalDictation.csproj') -c Release -r win-x64 --self-contained true -o $Output --packages (Join-Path $PSScriptRoot '.tools\packages')
if($LASTEXITCODE -ne 0){throw 'App build failed'}
& $dotnet publish (Join-Path $PSScriptRoot 'src\NativeWorker\NativeWorker.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $Output 'native') --packages (Join-Path $PSScriptRoot '.tools\packages')
if($LASTEXITCODE -ne 0){throw 'Worker build failed'}
# Whisper.net and sherpa-onnx bring libraries for every platform; a win-x64 worker only loads runtimes\win-x64 and runtimes\<backend>\win-x64.
$runtimes=Join-Path $Output 'native\runtimes'
if(Test-Path -LiteralPath $runtimes){
$backends=@(Get-ChildItem -LiteralPath $runtimes -Directory | Where-Object Name -notmatch '-')
$foreign=@(Get-ChildItem -LiteralPath $runtimes -Directory)+@($backends | ForEach-Object {Get-ChildItem -LiteralPath $_.FullName -Directory}) | Where-Object {$_.Name -match '^(android|browser|freebsd|ios|linux|maccatalyst|osx|tvos|win)-' -and $_.Name -ne 'win-x64'}
foreach($dir in $foreign){Remove-Item -LiteralPath $dir.FullName -Recurse -Force}
foreach($dir in $backends){if(-not(Get-ChildItem -LiteralPath $dir.FullName -Force)){Remove-Item -LiteralPath $dir.FullName -Force}}
}
# The about page opens the license notices next to the exe.
foreach($name in @('LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $Output -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $Output -Recurse -Force
Write-Host "Native app built: $Output\LocalDictation.exe"
