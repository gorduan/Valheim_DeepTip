# Builds DeepTip for Valheim and packs it for Thunderstore and r2modman ("Import local mod").
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Out <folder>]
#
# Output: <Out>\Gorduan-DeepTip\ (package folder) and <Out>\Gorduan-DeepTip-<version>.zip
# Checks: the glossary with the DeepTip validator (tools\deeptip-check.mjs of https://github.com/gorduan/DeepTip, Node 18+;
# found via the environment variable DEEPTIP_REPO or a DeepTip checkout next to this repository), the manifest
# description (max 250 chars), no BOM in manifest.json / README.md / CHANGELOG.md (Thunderstore rejects it).
param([string]$Out = (Join-Path $PSScriptRoot 'build-output'))

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)

$plugin = Get-Content (Join-Path $root 'src\Plugin.cs') -Raw
$version = [regex]::Match($plugin, 'const string Version = "([^"]+)"').Groups[1].Value
if (-not $version) { Write-Host 'Version not found in src\Plugin.cs' -ForegroundColor Red; exit 1 }

dotnet build (Join-Path $root 'GC_Valheim_DeepTip.csproj') -c Release -nologo -v:q | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed' -ForegroundColor Red; exit 1 }

$glossary = @(Get-ChildItem (Join-Path $root 'glossary') -Filter '*.json' -ErrorAction SilentlyContinue)
$candidates = @()
if ($env:DEEPTIP_REPO) { $candidates += Join-Path $env:DEEPTIP_REPO 'tools\deeptip-check.mjs' }
$candidates += @((Join-Path $root '..\DeepTip\tools\deeptip-check.mjs'), (Join-Path $root '..\..\..\..\DeepTip\tools\deeptip-check.mjs'))
$check = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $check) { Write-Host 'DeepTip validator not found (set DEEPTIP_REPO): glossary not checked' -ForegroundColor Yellow }
if ($glossary.Count -gt 0 -and $check -and (Get-Command node -ErrorAction SilentlyContinue)) {
    node $check --base de @($glossary | ForEach-Object { $_.FullName }) | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host 'Glossary check failed' -ForegroundColor Red; exit 1 }
}

$description = 'Paradox-style nested tooltips: Valheim tooltips open beside the item, you can move into them, highlighted terms open further explanations, click pins. Valheim glossary (EN/DE), lexicon in the Compendium, API for every mod.'
if ($description.Length -gt 250) { Write-Host "Description has $($description.Length) characters, max 250" -ForegroundColor Red; exit 1 }

$folder = Join-Path $Out 'Gorduan-DeepTip'
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
New-Item -ItemType Directory -Force -Path $folder | Out-Null
Copy-Item (Join-Path $root 'bin\GC_Valheim_DeepTip.dll') $folder
foreach ($file in 'README.md', 'CHANGELOG.md', 'LICENSE', 'icon.png') {
    $source = Join-Path $root $file
    if (-not (Test-Path $source)) { Write-Host "missing: $file" -ForegroundColor Red; exit 1 }
    if ($file -like '*.md') {
        $text = [IO.File]::ReadAllText($source)
        [IO.File]::WriteAllText((Join-Path $folder $file), $text.TrimStart([char]0xFEFF), $utf8)
    } else {
        Copy-Item $source $folder
    }
}
$manifest = [ordered]@{
    name = 'DeepTip'
    version_number = $version
    website_url = 'https://github.com/gorduan/Valheim_DeepTip'
    description = $description
    dependencies = @('denikson-BepInExPack_Valheim-5.4.2350')
}
[IO.File]::WriteAllText((Join-Path $folder 'manifest.json'), ($manifest | ConvertTo-Json -Depth 4), $utf8)

$zip = Join-Path $Out "Gorduan-DeepTip-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($file in Get-ChildItem $folder -File -Recurse | Sort-Object FullName) {
        $entry = $file.FullName.Substring($folder.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
Write-Host "DeepTip $version -> $zip"
