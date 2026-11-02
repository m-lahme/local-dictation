#Requires -Version 5.1
param([string]$Source='',[string]$Target=(Join-Path $env:LOCALAPPDATA 'Programs\Local Dictation'),[switch]$NoShortcut,[switch]$Uninstall,[switch]$RemoveData)
$ErrorActionPreference='Stop'
if($RemoveData -and -not $Uninstall){throw '-RemoveData can only be used together with -Uninstall.'}
$Target=[IO.Path]::GetFullPath($Target)
if($Target.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Target).TrimEnd('\')){throw 'A drive root cannot be the installation folder.'}
$Target=$Target.TrimEnd('\');$parent=Split-Path $Target -Parent;$session=(Get-Process -Id $PID).SessionId
$shortcutPath=Join-Path ([Environment]::GetFolderPath('Programs')) 'Local Dictation.lnk'
function Test-Inside([string]$path){try{[IO.Path]::GetFullPath($path).StartsWith($Target+'\',[StringComparison]::OrdinalIgnoreCase)}catch{$false}}
function Get-Running{@(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.SessionId -eq $session -and $_.Path -and (Test-Inside $_.Path)})}
# Backup (.previous-<guid>) and staging (.new-<guid>) folders that setup creates next to the installation.
function Get-SetupFolders([string]$kind){if(Test-Path -LiteralPath $parent){@(Get-ChildItem -LiteralPath $parent -Directory | Where-Object Name -match ('^'+[regex]::Escape((Split-Path $Target -Leaf))+'\.('+$kind+')-[0-9a-f]{32}$'))}else{@()}}
if($Uninstall){
if((Test-Path -LiteralPath $Target) -and -not(Test-Path -LiteralPath (Join-Path $Target 'release-manifest.json')) -and @(Get-ChildItem -LiteralPath $Target -Recurse -File -Force).Count){throw "Not a recognized installation folder, nothing was removed: $Target"}
$data=[IO.Path]::GetFullPath($(if($env:LOCAL_DICTATION_DATA){$env:LOCAL_DICTATION_DATA}else{Join-Path $env:LOCALAPPDATA 'LocalDictation'})).TrimEnd('\')
if($RemoveData -and (Test-Path -LiteralPath $data)){
if(-not(@('settings.json','history.json','native-models') | Where-Object {Test-Path -LiteralPath (Join-Path $data $_)})){throw "Not a recognized Local Dictation data folder, nothing was removed: $data"}
if($data -eq [IO.Path]::GetPathRoot($data+'\').TrimEnd('\') -or (@($env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA) | Where-Object {$_ -and ($_.TrimEnd('\')+'\').StartsWith($data+'\',[StringComparison]::OrdinalIgnoreCase)})){throw "This data folder will not be deleted: $data"}
# A copy running from another folder (portable or development build) may still use the data folder.
if(Get-Process -Name LocalDictation,LocalDictation.Worker -ErrorAction SilentlyContinue | Where-Object {$_.SessionId -eq $session -and -not($_.Path -and (Test-Inside $_.Path))}){throw "Another Local Dictation instance is still running and may be using $data. Quit it and try again; nothing was removed."}
}
# --shutdown reaches whichever instance runs in this session, so it is only sent when that instance is the installed copy.
if(Get-Running | Where-Object Name -eq 'LocalDictation'){Start-Process -FilePath (Join-Path $Target 'LocalDictation.exe') -ArgumentList '--shutdown' -WindowStyle Hidden;$deadline=[DateTime]::UtcNow.AddSeconds(10);while(@(Get-Running).Count -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}}
if(@(Get-Running).Count){throw 'Local Dictation did not quit within ten seconds. Quit it from the tray menu and try again.'}
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command=[string](Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).LocalDictation
if($command){$command=$command.Trim();$exe=if($command.StartsWith('"')){$command.Split('"')[1]}else{$command.Split(' ')[0]};if(Test-Inside $exe){Remove-ItemProperty -LiteralPath $runKey -Name LocalDictation}}
if((Test-Path -LiteralPath $shortcutPath) -and (Test-Inside ((New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath).TargetPath))){Remove-Item -LiteralPath $shortcutPath -Force}
foreach($folder in Get-SetupFolders 'previous|new'){Remove-Item -LiteralPath $folder.FullName -Recurse -Force}
if($RemoveData -and (Test-Path -LiteralPath $data)){Remove-Item -LiteralPath $data -Recurse -Force}
if(Test-Inside ((Get-Location -PSProvider FileSystem).ProviderPath+'\')){Set-Location -LiteralPath $parent}
if(Test-Inside ([Environment]::CurrentDirectory+'\')){[Environment]::CurrentDirectory=$parent}
# The manifest is removed last, so an interrupted uninstall can simply be repeated.
if(Test-Path -LiteralPath $Target){try{Get-ChildItem -LiteralPath $Target -Force | Where-Object Name -ne 'release-manifest.json' | Remove-Item -Recurse -Force;Remove-Item -LiteralPath $Target -Recurse -Force}catch{throw "The installation folder is still in use and was not completely removed: $Target. Close any windows and consoles in this folder, then delete it."}}
if($RemoveData){Write-Host "Local Dictation was removed, including settings, history and models in $data."}else{Write-Host "Local Dictation was removed. Settings, history and models remain in $data; -RemoveData deletes them."}
return
}
if(-not $Source){$Source=if(Test-Path (Join-Path $PSScriptRoot 'LocalDictation.exe')){$PSScriptRoot}else{Join-Path $PSScriptRoot 'bin'}}
$Source=[IO.Path]::GetFullPath($Source)
if($Source.StartsWith($Target+'\',[StringComparison]::OrdinalIgnoreCase) -or $Target -eq $Source -or $Target.StartsWith($Source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'The installation folder must be outside the package.'}
if(-not(Test-Path (Join-Path $Source 'LocalDictation.exe'))){throw "LocalDictation.exe is missing from $Source."}
$manifestPath=Join-Path $Source 'release-manifest.json'
if(-not(Test-Path $manifestPath)){throw 'release-manifest.json is missing. Create the package with package.ps1.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach($file in $manifest.files){$path=[IO.Path]::GetFullPath((Join-Path $Source $file.path));if(-not $path.StartsWith($Source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid path in the manifest'};if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw "Integrity check failed: $($file.path)"}}
if(@(Get-Running).Count){throw 'The installed app is still running. Quit it from the tray menu and run setup again.'}
if((Test-Path $Target) -and -not(Test-Path (Join-Path $Target 'release-manifest.json'))){if(@(Get-ChildItem -LiteralPath $Target -Recurse -File -Force).Count){throw 'The existing target folder is not a recognized installation and was left unchanged.'};Remove-Item -LiteralPath $Target -Recurse -Force}
New-Item -ItemType Directory -Force $parent | Out-Null
$stage=$Target+'.new-'+[Guid]::NewGuid().ToString('N')
$backup=$Target+'.previous-'+[Guid]::NewGuid().ToString('N')
try{
New-Item -ItemType Directory $stage | Out-Null
foreach($file in $manifest.files){$dest=Join-Path $stage $file.path;New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null;Copy-Item -LiteralPath (Join-Path $Source $file.path) -Destination $dest}
Copy-Item -LiteralPath $manifestPath -Destination $stage
if(Test-Path $Target){Move-Item -LiteralPath $Target -Destination $backup}
try{Move-Item -LiteralPath $stage -Destination $Target}catch{if(Test-Path $backup){Move-Item -LiteralPath $backup -Destination $Target};throw}
}finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
# Keep only the backup of the installation that was just replaced.
if(Test-Path -LiteralPath $backup){foreach($old in Get-SetupFolders 'previous' | Where-Object FullName -ne $backup){try{Remove-Item -LiteralPath $old.FullName -Recurse -Force}catch{Write-Warning "Could not remove an older backup folder: $($old.FullName)"}}}
if(-not $NoShortcut){$shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($shortcutPath);$shortcut.TargetPath=Join-Path $Target 'LocalDictation.exe';$shortcut.WorkingDirectory=$Target;$shortcut.IconLocation=$shortcut.TargetPath;$shortcut.Save()}
Write-Host "Installed to $Target. Settings and models are kept. Autostart can be turned on in the app."
if(Test-Path -LiteralPath $backup){Write-Host "The previous installation is kept as a fallback folder: $backup"}
