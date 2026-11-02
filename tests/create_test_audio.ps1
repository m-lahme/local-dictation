$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$dir = Join-Path $PSScriptRoot 'audio'
New-Item -ItemType Directory -Force $dir | Out-Null
$cases = @(
  @{ Name = 'de_kurz'; Text = 'Dies ist ein automatischer Test für Local Dictation.' },
  @{ Name = 'de_fachbegriffe'; Text = 'Claude Code öffnet ein Repository und prüft die Konfiguration.' },
  @{ Name = 'en_short'; Text = 'This is an automated transcription test.' }
)
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
foreach ($case in $cases) {
  $path = Join-Path $dir ($case.Name + '.wav')
  $voice.SetOutputToWaveFile($path)
  $voice.Speak($case.Text)
  $voice.SetOutputToNull()
}
$voice.Dispose()
Write-Host "Created $($cases.Count) deterministic audio fixtures in $dir"
