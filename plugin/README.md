# @local/dsh-ue-asset

DeepSeek Harness bundle for [dsh-ue-asset](../README.md).

- **Host** (`index.js`): owns one `ueasset serve` child, registers 8 native tools, exposes `/ue-asset/api/*`.
- **Client** (`client.js`): right-sidebar tab, settings section, job bar, tool cards.

## Install

```powershell
dsh plugin --profile desktop add link:E:/WorkSpace/Tools/dsh-ue-asset/plugin
```

Then add `"@local/dsh-ue-asset"` to `dsh.profile.bundles` in the profile's `package.json`.

## Row config (all optional)

```yaml
- insert:
    - id: ue-asset
      name: '@local/dsh-ue-asset'
      config:
        configPath: C:/Users/you/AppData/Local/dsh-ue-asset/config.json
        exePath: E:/WorkSpace/Tools/dsh-ue-asset/src/DshUeAsset.Cli/bin/Release/net10.0/ueasset.dll
        dotnetPath: C:/Program Files/dotnet/dotnet.exe
```

Defaults: config from `%LOCALAPPDATA%\dsh-ue-asset\config.json`; exe from `<repo>/dist/ueasset.exe`
when published, otherwise the Release DLL built by `dotnet build`.
