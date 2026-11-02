#Requires -Version 5.1
param([string]$Output = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
& $dotnet publish (Join-Path $PSScriptRoot 'src\LocalDictation\LocalDictation.csproj') -c Release -r win-x64 --self-contained true -o $Output --packages (Join-Path $PSScriptRoot '.tools\packages')
if($LASTEXITCODE -ne 0){throw 'App build failed'}
& $dotnet publish (Join-Path $PSScriptRoot 'src\NativeWorker\NativeWorker.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $Output 'native') --packages (Join-Path $PSScriptRoot '.tools\packages')
if($LASTEXITCODE -ne 0){throw 'Worker build failed'}
# The sherpa-onnx NuGet runtime also contains text-to-speech code (espeak-ng, GPL-3.0). The worker only uses speech recognition, so it gets the official sherpa-onnx build without text-to-speech.
$sherpa='sherpa-onnx-v1.13.5-win-x64-shared-MT-Release-no-tts-lib'; $sherpaHash='B279D43DDB09F845FC67A993678D4FED27D0AB325D3EF75E045C41D4FD05FB27'
$tools=Join-Path $PSScriptRoot '.tools'; $archive=Join-Path $tools "$sherpa.tar.bz2"
if(!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $sherpaHash){
New-Item -ItemType Directory -Force -Path $tools | Out-Null; $partial="$archive.partial"
[Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.5/$sherpa.tar.bz2" -OutFile $partial
if((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $sherpaHash){Remove-Item -LiteralPath $partial -Force; throw 'The sherpa-onnx download has an unexpected SHA-256 hash'}
Move-Item -LiteralPath $partial -Destination $archive -Force
}
$unpacked=Join-Path $tools $sherpa; if(Test-Path -LiteralPath $unpacked){Remove-Item -LiteralPath $unpacked -Recurse -Force}
& (Join-Path $env:SystemRoot 'System32\tar.exe') -xjf $archive -C $tools
if($LASTEXITCODE -ne 0){throw 'Could not unpack sherpa-onnx'}
foreach($target in @((Join-Path $Output 'native'),(Join-Path $Output 'native\runtimes\win-x64\native'))){if(Test-Path -LiteralPath (Join-Path $target 'sherpa-onnx-c-api.dll')){foreach($dll in @('sherpa-onnx-c-api.dll','onnxruntime.dll')){Copy-Item -LiteralPath (Join-Path $unpacked "lib\$dll") -Destination $target -Force}}}
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
