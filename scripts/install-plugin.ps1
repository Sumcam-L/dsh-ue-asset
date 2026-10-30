# Installs this bundle into an Electron-managed DSH profile.
#
# The packaged DSH CLI refuses to write such a profile ("managed exclusively by the
# Electron application"), so this script performs the same three edits by hand:
#   1. junction <profile>/node_modules/dsh-ue-asset -> <repo>/plugin
#   2. dependency "dsh-ue-asset": "link:<repo>/plugin"
#   3. "dsh-ue-asset" in dsh.profile.bundles
#
# Dry run by default. Pass -Apply to write. Restart DSH afterwards.
[CmdletBinding()]
param(
    [string]$Profile = 'desktop',
    [switch]$Apply,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $repo 'plugin'
$profileDir = Join-Path $env:USERPROFILE ".dsh/profiles/$Profile"
$packagePath = Join-Path $profileDir 'package.json'
$linkPath = Join-Path $profileDir 'node_modules/dsh-ue-asset'
$name = 'dsh-ue-asset'
$spec = 'link:' + ($plugin -replace '\\', '/')

if (-not (Test-Path $packagePath)) { throw "profile package.json not found: $packagePath" }
$package = Get-Content $packagePath -Raw | ConvertFrom-Json

Write-Host "profile : $profileDir"
Write-Host "plugin  : $plugin"
Write-Host "link    : $spec"
Write-Host ("bundled : " + ($package.dsh.profile.bundles -contains $name))
Write-Host ("dep     : " + [bool]$package.dependencies.$name)
Write-Host ("junction: " + (Test-Path $linkPath))

if (-not $Apply) {
    Write-Host ''
    Write-Host 'Dry run. Re-run with -Apply to install, or -Apply -Uninstall to remove.'
    return
}

if ($Uninstall) {
    if (Test-Path $linkPath) { (Get-Item $linkPath).Delete() }
    $package.dependencies.PSObject.Properties.Remove($name)
    $package.dsh.profile.bundles = @($package.dsh.profile.bundles | Where-Object { $_ -ne $name })
} else {
    if (-not (Test-Path (Split-Path $linkPath))) { New-Item -ItemType Directory -Path (Split-Path $linkPath) -Force | Out-Null }
    if (-not (Test-Path $linkPath)) {
        New-Item -ItemType Junction -Path $linkPath -Target $plugin | Out-Null
    }
    $package.dependencies | Add-Member -NotePropertyName $name -NotePropertyValue $spec -Force
    if ($package.dsh.profile.bundles -notcontains $name) {
        $package.dsh.profile.bundles = @($package.dsh.profile.bundles) + $name
    }
}

$json = $package | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($packagePath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'Written. Restart DSH to load the bundle.'