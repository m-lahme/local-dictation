[CmdletBinding()]
param(
    [ValidateSet('whisper', 'parakeet')][string]$Engine,
    [ValidateSet('gpu', 'cpu')][string]$Device,
    [ValidateSet('system', 'light', 'dark')][string]$Theme,
    [ValidateSet('Insert', 'Clipboard', 'Both')][string]$Output,
    [ValidateSet('bottom-center', 'bottom-left', 'bottom-right', 'top-center', 'top-left', 'top-right')][string]$Anchor,
    [string]$Monitor,
    [string]$Microphone,
    [ValidateSet('ctrl-win', 'ctrl-shift-space')][string]$Hotkey,
    [ValidateRange(200, 1000)][int]$HoldMs,
    [Nullable[bool]]$OverlayVisible,
    [Nullable[bool]]$AutomaticPowerModel,
    [switch]$Show,
    [switch]$Restart,
    [switch]$AllowInjectedHotkey,
    [string]$DataDir = (Join-Path $env:LOCALAPPDATA 'LocalDictation'),
    [string]$Executable = (Join-Path $PSScriptRoot 'bin\LocalDictation.exe')
)

$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $DataDir 'settings.json'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "Einstellungsdatei nicht gefunden: $settingsPath"
}

if ($Show -and $PSBoundParameters.Keys.Count -eq 1) {
    Get-Content -LiteralPath $settingsPath -Raw
    return
}

$changedKeys = @($PSBoundParameters.Keys | Where-Object { $_ -notin @('Show', 'Restart', 'AllowInjectedHotkey', 'DataDir', 'Executable') })
if ($changedKeys.Count -eq 0 -and -not $Restart) {
    Get-Content -LiteralPath $settingsPath -Raw
    return
}

$running = @(Get-Process -Name LocalDictation -ErrorAction SilentlyContinue)
if ($running.Count -gt 0 -and -not $Restart) {
    throw 'Local Dictation läuft. Verwende -Restart, damit die App kontrolliert beendet, geändert und neu gestartet wird.'
}

if ($running.Count -gt 0) {
    if (-not (Test-Path -LiteralPath $Executable)) { throw "App nicht gefunden: $Executable" }
    Start-Process -FilePath $Executable -ArgumentList '--shutdown' -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $running = @(Get-Process -Name LocalDictation -ErrorAction SilentlyContinue)
    } while ($running.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($running.Count -gt 0) { throw 'Local Dictation ließ sich nicht innerhalb von zehn Sekunden beenden.' }
}

if ($changedKeys.Count -gt 0) {
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    foreach ($key in $changedKeys) {
        $value = $PSBoundParameters[$key]
        if ($key -eq 'Output') { $value = @{ Insert = 0; Clipboard = 1; Both = 2 }[$value] }
        if ($settings.PSObject.Properties.Name -contains $key) { $settings.$key = $value }
        else { $settings | Add-Member -NotePropertyName $key -NotePropertyValue $value }
    }
    if ($settings.Engine -eq 'whisper') { $settings.Device = 'gpu' }
    else { $settings.Engine = 'parakeet'; $settings.Device = 'cpu' }
    if ($Microphone) {
        # The app uses the first connected entry of the priority list; -Microphone moves that endpoint to the top.
        $entries = @($settings.Microphones | Where-Object { $_ })
        $entry = $entries | Where-Object { $_.Ids -contains $Microphone } | Select-Object -First 1
        if (-not $entry) { $entry = [pscustomobject]@{ Key = $Microphone; Ids = @($Microphone); Name = ''; Description = ''; Device = ''; Kind = ''; Connection = '' } }
        $settings | Add-Member -NotePropertyName Microphones -NotePropertyValue (@($entry) + @($entries | Where-Object { $_ -ne $entry })) -Force
    }

    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    $temporary = "$settingsPath.new"
    $json = $settings | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $settingsPath, $true)
}

if ($Restart) {
    $arguments = if ($AllowInjectedHotkey) { @('--autostart', '--allow-injected-hotkey') } else { @('--autostart') }
    Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden
}

if ($Show) { Get-Content -LiteralPath $settingsPath -Raw }
