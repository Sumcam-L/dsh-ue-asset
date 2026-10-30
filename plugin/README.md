# dsh-ue-asset

DeepSeek Harness bundle for [dsh-ue-asset](../README.md) — browse, preview and export
Unreal Engine 4/5 cooked assets straight out of local PAK archives (CUE4Parse).

- **Host** (`index.js`): owns one `ueasset serve` child process, registers 17 `uea_*` tools
  and exposes `/ue-asset/api/*` on the DSH web server.
- **Client** (`client.js`): right-sidebar browser/preview tab, settings section, job bar,
  and native tool cards for every `uea_*` call.

## Install

Prebuilt bundle (recommended — it carries `dist/ueasset.exe`):

```powershell
dsh plugin --profile desktop add https://github.com/Sumcam-L/dsh-ue-asset/releases/latest/download/dsh-ue-asset.tgz
```

or from npm:

```powershell
dsh plugin --profile desktop add dsh-ue-asset
```

Either way, add `"dsh-ue-asset"` to `dsh.profile.bundles` in the profile's `package.json`
and restart DSH.

Installing from a checkout (the plugin has to find the built CLI):

```powershell
dsh plugin --profile desktop add "github:Sumcam-L/dsh-ue-asset#path:/plugin"
```

A git install only receives the `plugin/` directory, so build the CLI first
(`dotnet publish src/DshUeAsset.Cli -c Dist -r win-x64 -o dist`) or point `exePath` at an
already-published `ueasset.exe`.

## Row config (all optional)

```yaml
- insert:
    - id: ue-asset
      name: 'dsh-ue-asset'
      config:
        configPath: C:/Users/you/AppData/Local/dsh-ue-asset/config.json
        exePath: E:/WorkSpace/Tools/dsh-ue-asset/src/DshUeAsset.Cli/bin/Release/net10.0/ueasset.dll
        dotnetPath: C:/Program Files/dotnet/dotnet.exe
```

Defaults: config from `%LOCALAPPDATA%\dsh-ue-asset\config.json`; the newest
`ueasset*.exe` in `<package>/dist` (flat install) or the repo's `dist` (checkout), otherwise
the Release DLL built by `dotnet build`.

## Requirements

Windows x64. Oodle (`oo2core_9_win64.dll`) is Epic-proprietary and is **not** bundled — the
settings page has a *Download* button that fetches it once, or point `oodleLibrary` at a copy
you already have.
