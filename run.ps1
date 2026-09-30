$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
& "$root\bin\LocalDictation.exe"
