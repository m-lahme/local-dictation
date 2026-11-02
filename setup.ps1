#Requires -Version 5.1
param([string]$Source='',[string]$Target=(Join-Path $env:LOCALAPPDATA 'Programs\Local Dictation'),[switch]$NoShortcut,[switch]$Uninstall,[switch]$RemoveData)
$ErrorActionPreference='Stop'
if($RemoveData -and -not $Uninstall){throw '-RemoveData ist nur zusammen mit -Uninstall möglich.'}
$Target=[IO.Path]::GetFullPath($Target)
if($Target.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Target).TrimEnd('\')){throw 'Ein Laufwerksstamm ist kein Installationsordner.'}
$Target=$Target.TrimEnd('\');$parent=Split-Path $Target -Parent;$session=(Get-Process -Id $PID).SessionId
$shortcutPath=Join-Path ([Environment]::GetFolderPath('Programs')) 'Local Dictation.lnk'
function Test-Inside([string]$path){try{[IO.Path]::GetFullPath($path).StartsWith($Target+'\',[StringComparison]::OrdinalIgnoreCase)}catch{$false}}
function Get-Running{@(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.SessionId -eq $session -and $_.Path -and (Test-Inside $_.Path)})}
# Backup (.previous-<guid>) and staging (.new-<guid>) folders that setup creates next to the installation.
function Get-SetupFolders([string]$kind){if(Test-Path -LiteralPath $parent){@(Get-ChildItem -LiteralPath $parent -Directory | Where-Object Name -match ('^'+[regex]::Escape((Split-Path $Target -Leaf))+'\.('+$kind+')-[0-9a-f]{32}$'))}else{@()}}
if($Uninstall){
if((Test-Path -LiteralPath $Target) -and -not(Test-Path -LiteralPath (Join-Path $Target 'release-manifest.json')) -and @(Get-ChildItem -LiteralPath $Target -Recurse -File -Force).Count){throw "Kein erkannter Installationsordner, nichts entfernt: $Target"}
$data=[IO.Path]::GetFullPath($(if($env:LOCAL_DICTATION_DATA){$env:LOCAL_DICTATION_DATA}else{Join-Path $env:LOCALAPPDATA 'LocalDictation'})).TrimEnd('\')
if($RemoveData -and (Test-Path -LiteralPath $data)){
if(-not(@('settings.json','history.json','native-models') | Where-Object {Test-Path -LiteralPath (Join-Path $data $_)})){throw "Kein Datenordner von Local Dictation erkannt, nichts entfernt: $data"}
if($data -eq [IO.Path]::GetPathRoot($data+'\').TrimEnd('\') -or (@($env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA) | Where-Object {$_ -and ($_.TrimEnd('\')+'\').StartsWith($data+'\',[StringComparison]::OrdinalIgnoreCase)})){throw "Dieser Datenordner wird nicht gelöscht: $data"}
# A copy running from another folder (portable or development build) may still use the data folder.
if(Get-Process -Name LocalDictation,LocalDictation.Worker -ErrorAction SilentlyContinue | Where-Object {$_.SessionId -eq $session -and -not($_.Path -and (Test-Inside $_.Path))}){throw "Eine andere Local-Dictation-Instanz läuft noch und nutzt womöglich $data. Bitte sie beenden und erneut versuchen; es wurde nichts entfernt."}
}
# --shutdown reaches whichever instance runs in this session, so it is only sent when that instance is the installed copy.
if(Get-Running | Where-Object Name -eq 'LocalDictation'){Start-Process -FilePath (Join-Path $Target 'LocalDictation.exe') -ArgumentList '--shutdown' -WindowStyle Hidden;$deadline=[DateTime]::UtcNow.AddSeconds(10);while(@(Get-Running).Count -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}}
if(@(Get-Running).Count){throw 'Local Dictation ließ sich nicht innerhalb von zehn Sekunden beenden. Bitte über das Tray-Menü beenden und erneut versuchen.'}
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command=[string](Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).LocalDictation
if($command){$command=$command.Trim();$exe=if($command.StartsWith('"')){$command.Split('"')[1]}else{$command.Split(' ')[0]};if(Test-Inside $exe){Remove-ItemProperty -LiteralPath $runKey -Name LocalDictation}}
if((Test-Path -LiteralPath $shortcutPath) -and (Test-Inside ((New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath).TargetPath))){Remove-Item -LiteralPath $shortcutPath -Force}
foreach($folder in Get-SetupFolders 'previous|new'){Remove-Item -LiteralPath $folder.FullName -Recurse -Force}
if($RemoveData -and (Test-Path -LiteralPath $data)){Remove-Item -LiteralPath $data -Recurse -Force}
if(Test-Inside ((Get-Location -PSProvider FileSystem).ProviderPath+'\')){Set-Location -LiteralPath $parent}
if(Test-Inside ([Environment]::CurrentDirectory+'\')){[Environment]::CurrentDirectory=$parent}
# The manifest is removed last, so an interrupted uninstall can simply be repeated.
if(Test-Path -LiteralPath $Target){try{Get-ChildItem -LiteralPath $Target -Force | Where-Object Name -ne 'release-manifest.json' | Remove-Item -Recurse -Force;Remove-Item -LiteralPath $Target -Recurse -Force}catch{throw "Der Installationsordner wird noch verwendet und wurde nicht vollständig entfernt: $Target. Bitte Fenster und Konsolen in diesem Ordner schließen und ihn anschließend löschen."}}
if($RemoveData){Write-Host "Local Dictation wurde entfernt, einschließlich Einstellungen, Verlauf und Modellen in $data."}else{Write-Host "Local Dictation wurde entfernt. Einstellungen, Verlauf und Modelle bleiben in $data erhalten; -RemoveData löscht sie."}
return
}
if(-not $Source){$Source=if(Test-Path (Join-Path $PSScriptRoot 'LocalDictation.exe')){$PSScriptRoot}else{Join-Path $PSScriptRoot 'bin'}}
$Source=[IO.Path]::GetFullPath($Source)
if($Source.StartsWith($Target+'\',[StringComparison]::OrdinalIgnoreCase) -or $Target -eq $Source -or $Target.StartsWith($Source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Der Installationsordner muss außerhalb des Pakets liegen.'}
if(-not(Test-Path (Join-Path $Source 'LocalDictation.exe'))){throw "LocalDictation.exe fehlt in $Source."}
$manifestPath=Join-Path $Source 'release-manifest.json'
if(-not(Test-Path $manifestPath)){throw 'release-manifest.json fehlt. Das Paket mit package.ps1 erzeugen.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach($file in $manifest.files){$path=[IO.Path]::GetFullPath((Join-Path $Source $file.path));if(-not $path.StartsWith($Source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Ungültiger Pfad im Manifest'};if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw "Integritätsprüfung fehlgeschlagen: $($file.path)"}}
if(@(Get-Running).Count){throw 'Die installierte App läuft noch. Bitte über das Tray-Menü beenden und Setup erneut starten.'}
if((Test-Path $Target) -and -not(Test-Path (Join-Path $Target 'release-manifest.json'))){if(@(Get-ChildItem -LiteralPath $Target -Recurse -File -Force).Count){throw 'Der vorhandene Zielordner ist keine erkannte Installation und bleibt unverändert.'};Remove-Item -LiteralPath $Target -Recurse -Force}
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
if(Test-Path -LiteralPath $backup){foreach($old in Get-SetupFolders 'previous' | Where-Object FullName -ne $backup){try{Remove-Item -LiteralPath $old.FullName -Recurse -Force}catch{Write-Warning "Älterer Sicherungsordner wurde nicht entfernt: $($old.FullName)"}}}
if(-not $NoShortcut){$shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($shortcutPath);$shortcut.TargetPath=Join-Path $Target 'LocalDictation.exe';$shortcut.WorkingDirectory=$Target;$shortcut.IconLocation=$shortcut.TargetPath;$shortcut.Save()}
Write-Host "Installiert in $Target. Einstellungen und Modelle bleiben erhalten. Autostart lässt sich in der App einschalten."
if(Test-Path -LiteralPath $backup){Write-Host "Die vorherige Installation bleibt als Rückfallordner erhalten: $backup"}
