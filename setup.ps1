param([string]$Source='',[string]$Target=(Join-Path $env:LOCALAPPDATA 'Programs\Local Dictation'),[switch]$NoShortcut)
$ErrorActionPreference='Stop'
if(-not $Source){$Source=if(Test-Path (Join-Path $PSScriptRoot 'LocalDictation.exe')){$PSScriptRoot}else{Join-Path $PSScriptRoot 'bin'}}
$Source=[IO.Path]::GetFullPath($Source)
$Target=[IO.Path]::GetFullPath($Target)
if($Target.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Target).TrimEnd('\')){throw 'A drive root is not an installation directory'}
if($Source.StartsWith($Target+'\',[StringComparison]::OrdinalIgnoreCase) -or $Target -eq $Source -or $Target.StartsWith($Source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Installation target must be outside the source build'}
if(-not(Test-Path (Join-Path $Source 'LocalDictation.exe'))){throw 'Build the native app first'}
$manifestPath=Join-Path $Source 'release-manifest.json'
if(-not(Test-Path $manifestPath)){throw 'Missing release manifest. Run package.ps1 first.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach($file in $manifest.files){$path=[IO.Path]::GetFullPath((Join-Path $Source $file.path));if(-not $path.StartsWith($Source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid manifest path'};if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw "Release integrity check failed: $($file.path)"}}
if(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith($Target+'\',[StringComparison]::OrdinalIgnoreCase)}){throw 'Close the installed app before updating'}
$parent=Split-Path $Target -Parent
New-Item -ItemType Directory -Force $parent | Out-Null
$stage=$Target+'.new-'+[Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory $stage | Out-Null
foreach($file in $manifest.files){$dest=Join-Path $stage $file.path;New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null;Copy-Item -LiteralPath (Join-Path $Source $file.path) -Destination $dest}
Copy-Item -LiteralPath $manifestPath -Destination $stage
$backup=$Target+'.previous-'+[Guid]::NewGuid().ToString('N')
if(Test-Path $Target){if(-not(Test-Path (Join-Path $Target 'release-manifest.json'))){throw 'Existing target is not a recognized installation; left untouched'};Move-Item -LiteralPath $Target -Destination $backup}
try{Move-Item -LiteralPath $stage -Destination $Target}catch{if(Test-Path $backup){Move-Item -LiteralPath $backup -Destination $Target};throw}
if(-not $NoShortcut){$shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Local Dictation.lnk'));$shortcut.TargetPath=Join-Path $Target 'LocalDictation.exe';$shortcut.WorkingDirectory=$Target;$shortcut.IconLocation=$shortcut.TargetPath;$shortcut.Save()}
Write-Host "Installed $Target. Existing settings and models retained. Autostart is opt-in inside the app."
