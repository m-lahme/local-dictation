param(
    [string]$Source = (Join-Path $PSScriptRoot 'bin'),
    [string]$Destination = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$Source = [IO.Path]::GetFullPath($Source)
$Destination = [IO.Path]::GetFullPath($Destination)
if ($Destination -eq $Source -or $Destination.StartsWith($Source + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package destination must be outside the source build'
}
foreach ($required in @('LocalDictation.exe', 'native/LocalDictation.Worker.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $Source $required))) { throw "Build output missing: $required" }
}
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/LocalDictation/LocalDictation.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+([-.][a-zA-Z0-9.-]+)?$') { throw 'Invalid application version' }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$stage = [IO.Path]::GetFullPath((Join-Path $Destination ('package-' + [Guid]::NewGuid().ToString('N'))))
if (!$stage.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path' }
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Get-ChildItem -LiteralPath $Source | Where-Object Name -notin @('audit', 'release-manifest.json') |
        Copy-Item -Destination $stage -Recurse -Force
    foreach ($name in @('README.md', 'THIRD-PARTY-NOTICES.md', 'setup.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $stage -Recurse -Force
    $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath($stage, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    @{ version = $version; files = $files } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $stage 'release-manifest.json') -Encoding utf8
    $archive = Join-Path $Destination "LocalDictation-$version-win-x64.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryArchive = Join-Path $Destination ('package-' + [Guid]::NewGuid().ToString('N') + '.zip')
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryArchive)
        Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
    } finally {
        if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive -Force }
    }
    $hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
    "$($hash.Hash)  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
    $hash
} finally {
    # Only remove the unique staging directory created inside the package destination.
    if (!$stage.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $stage -Recurse -Force
}
