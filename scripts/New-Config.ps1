# Generates %LOCALAPPDATA%\dsh-ue-asset\config.json from FModel's saved AppSettings.json.
# FModel does not need to be running. AES keys are copied into our config and never printed.
[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$FmodelSettings
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $repo 'src/DshUeAsset.Cli/bin/Release/net10.0/ueasset.dll'

if (-not (Test-Path $cli)) {
    Push-Location $repo
    try { dotnet build src/DshUeAsset.Cli/DshUeAsset.Cli.csproj -c Release | Out-Host }
    finally { Pop-Location }
}

$args = @($cli, 'config', 'import-fmodel')
if ($ConfigPath) { $args = @('--config', $ConfigPath) + $args }
if ($FmodelSettings) { $args += @('--source', $FmodelSettings) }

dotnet @args
Write-Host ""
Write-Host "Config: $(if ($ConfigPath) { $ConfigPath } else { Join-Path $env:LOCALAPPDATA 'dsh-ue-asset/config.json' })"
Write-Host "Next: set oodleLibrary in that file, then run: ueasset open <profile>"
