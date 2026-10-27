# Publishes a self-contained single-file ueasset.exe into <repo>/dist.
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [switch]$NoSingleFile
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'dist'

$args = @(
    'publish', (Join-Path $repo 'src/DshUeAsset.Cli/DshUeAsset.Cli.csproj'),
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-o', $out
)

if ($NoSingleFile) { $args = $args | Where-Object { $_ -notlike 'PublishSingleFile*' } }

dotnet @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item (Join-Path $out 'ueasset.exe') (Join-Path $repo 'dist/ueasset.exe') -Force -ErrorAction SilentlyContinue
Write-Host "Published: $out/ueasset.exe"
