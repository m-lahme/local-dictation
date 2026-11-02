#Requires -Version 5.1
param(
    [string]$Destination = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LICENSE'))) { throw 'LICENSE is missing in the repository root. Add the project license before building a release package.' }
[xml]$props = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
$version = [string]$props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+([-.][a-zA-Z0-9.-]+)?$') { throw 'Invalid application version' }
# Only these publish outputs are shipped (*.pdb files are left out); anything else stops packaging instead of being released unreviewed.
$allowed = @(
    'LocalDictation\.(exe|dll|deps\.json|runtimeconfig\.json)', 'casefold\.json', 'model-catalog\.json',
    'native\\LocalDictation\.Worker\.(exe|dll|deps\.json|runtimeconfig\.json)', 'native\\runtimes\\(vulkan\\)?win-x64\\[^\\]+\.dll',
    '(native\\)?[^\\]+\.dll', '(native\\)?createdump\.exe', '(native\\)?[a-z]{2}(-[A-Za-z]+)?\\[^\\]+\.resources\.dll',
    'LICENSE', 'THIRD-PARTY-NOTICES\.md', 'licenses\\[^\\]+\.txt'
)
$allowedPattern = '^(' + ($allowed -join '|') + ')$'
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$id = [Guid]::NewGuid().ToString('N')
$publish = [IO.Path]::GetFullPath((Join-Path $Destination "publish-$id"))
$stage = [IO.Path]::GetFullPath((Join-Path $Destination "package-$id"))
foreach ($folder in @($publish, $stage)) { if (!$folder.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path' } }
try {
    # Always package a fresh publish: bin\ and reused output folders keep stale files from earlier builds.
    & (Join-Path $PSScriptRoot 'build.ps1') -Output $publish | Out-Host
    foreach ($required in @('LocalDictation.exe', 'native\LocalDictation.Worker.exe')) {
        $path = Join-Path $publish $required
        if (!(Test-Path -LiteralPath $path)) { throw "Build output missing: $required" }
        if (((Get-Item -LiteralPath $path).VersionInfo.ProductVersion -split '\+')[0] -ne $version) { throw "Build version of $required does not match $version" }
    }
    foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse) {
        $relative = $file.FullName.Substring($publish.Length + 1)
        if ($file.Extension -eq '.pdb') { continue }
        if ($relative -notmatch $allowedPattern) { throw "Unexpected file in build output: $relative" }
        $target = Join-Path $stage $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    foreach ($name in @('LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md', 'setup.ps1', 'setup.cmd')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $stage -Recurse -Force
    $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{ path = $_.FullName.Substring($stage.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    @{ version = $version; files = $files } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $stage 'release-manifest.json') -Encoding utf8
    $archive = Join-Path $Destination "LocalDictation-$version-win-x64.zip"
    Add-Type -AssemblyName System.IO.Compression
    $temporaryArchive = Join-Path $Destination "package-$id.zip"
    try {
        # Entries are added one by one so that their names use forward slashes in Windows PowerShell 5.1 as well.
        $stream = [IO.File]::Open($temporaryArchive, [IO.FileMode]::CreateNew)
        try {
            $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
            try {
                foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName) {
                    $entry = $zip.CreateEntry($file.FullName.Substring($stage.Length + 1).Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $file.LastWriteTime
                    $writer = $entry.Open(); $reader = [IO.File]::OpenRead($file.FullName)
                    try { $reader.CopyTo($writer) } finally { $reader.Dispose(); $writer.Dispose() }
                }
            } finally { $zip.Dispose() }
        } finally { $stream.Dispose() }
        Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
    } finally {
        if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive -Force }
    }
    $hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
    "$($hash.Hash)  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
    $hash
} finally {
    # Only remove the unique publish and staging directories created inside the package destination.
    foreach ($folder in @($publish, $stage)) { if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force } }
}
