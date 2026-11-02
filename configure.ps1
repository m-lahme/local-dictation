#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('whisper', 'parakeet')][string]$Engine,
    [ValidateSet('gpu', 'cpu')][string]$Device,
    [ValidateSet('system', 'light', 'dark')][string]$Theme,
    [ValidateSet('system', 'de', 'en')][string]$Language,
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
    [string]$DataDir = $(if ($env:LOCAL_DICTATION_DATA) { $env:LOCAL_DICTATION_DATA } else { Join-Path $env:LOCALAPPDATA 'LocalDictation' }),
    [string]$Executable
)

$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $DataDir 'settings.json'
$temporary = "$settingsPath.new"
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "Settings file not found: $settingsPath"
}

if ($Show -and $PSBoundParameters.Keys.Count -eq 1) {
    Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
    return
}

$changedKeys = @($PSBoundParameters.Keys | Where-Object { $_ -notin @('Show', 'Restart', 'AllowInjectedHotkey', 'DataDir', 'Executable') })
if ($changedKeys.Count -eq 0 -and -not $Restart) {
    Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
    return
}

$session = (Get-Process -Id $PID).SessionId
function Get-RunningApp { @(Get-Process -Name LocalDictation -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session }) }
$running = @(Get-RunningApp)
if ($running.Count -gt 0 -and -not $Restart) {
    throw 'Local Dictation is running. Use -Restart to quit the app cleanly, change the settings and start it again.'
}

if (-not $Executable) {
    # Restart the copy that is running; otherwise prefer this repository's build, then the copy installed by setup.ps1.
    $Executable = @(@($running | ForEach-Object { $_.Path }) + @((Join-Path $PSScriptRoot 'bin\LocalDictation.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Local Dictation\LocalDictation.exe')) | Where-Object { $_ -and (Test-Path -LiteralPath $_) })[0]
}
if ($Restart -and -not ($Executable -and (Test-Path -LiteralPath $Executable))) {
    throw $(if ($Executable) { "App not found: $Executable" } else { 'No built or installed LocalDictation.exe found. Specify the path with -Executable.' })
}

if ($running.Count -gt 0) {
    Start-Process -FilePath $Executable -ArgumentList '--shutdown' -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $running = @(Get-RunningApp)
    } while ($running.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($running.Count -gt 0) { throw 'Local Dictation did not quit within ten seconds.' }
}

try {
    if ($changedKeys.Count -gt 0) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
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
        $json = $settings | ConvertTo-Json -Depth 20
        [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
        # Windows PowerShell 5.1 has no File.Move overload with overwrite; File.Replace swaps the file in one step.
        if (Test-Path -LiteralPath $settingsPath) { [IO.File]::Replace($temporary, $settingsPath, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $settingsPath) }
    }
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    # Start the app again even if writing failed, so -Restart never leaves it shut down.
    if ($Restart) {
        $arguments = if ($AllowInjectedHotkey) { @('--autostart', '--allow-injected-hotkey') } else { @('--autostart') }
        Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden
    }
}

if ($Show) { Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 }
