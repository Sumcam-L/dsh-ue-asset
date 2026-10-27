param(
  [string]$OodleDll = "",
  [string]$OutDir = "dist-release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  $out = Join-Path $root $OutDir
  if (Test-Path $out) { Remove-Item $out -Recurse -Force }
  dotnet publish "src/DshUeAsset.Cli/DshUeAsset.Cli.csproj" -c Dist -r win-x64 --self-contained false -p:PublishSingleFile=true -o $out | Out-Host

  if (-not $OodleDll) {
    $configPath = Join-Path $env:LOCALAPPDATA "dsh-ue-asset/config.json"
    if (Test-Path $configPath) {
      $OodleDll = (Get-Content $configPath -Raw | ConvertFrom-Json).oodleLibrary
    }
  }
  if (-not $OodleDll) {
    $OodleDll = Get-ChildItem -Path $root, (Join-Path $root "dist") -Filter "oo2core*_win64.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
  }
  if ($OodleDll -and (Test-Path $OodleDll)) {
    Copy-Item $OodleDll (Join-Path $out (Split-Path $OodleDll -Leaf)) -Force
    Write-Host "oodle bundled: $OodleDll"
  } else {
    Write-Warning "Oodle DLL not found. Put oo2core_9_win64.dll next to ueasset.exe or pass -OodleDll."
  }

  $natives = Join-Path $root "dist/CUE4Parse-Natives.dll"
  if (-not (Test-Path $natives)) {
    $natives = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Temp/.net/FModel") -Recurse -Filter "CUE4Parse-Natives.dll" -ErrorAction SilentlyContinue |
      Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
  }
  if ($natives -and (Test-Path $natives)) {
    Copy-Item $natives (Join-Path $out "CUE4Parse-Natives.dll") -Force
    Write-Host "ACL natives bundled: $natives"
  } else {
    Write-Warning "CUE4Parse-Natives.dll not found; ACL-compressed animations will fail."
  }

  Copy-Item "plugin" (Join-Path $out "plugin") -Recurse -Force
  Write-Host "release ready: $out"
} finally {
  Pop-Location
}