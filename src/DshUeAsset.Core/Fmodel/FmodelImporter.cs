using CUE4Parse.UE4.Versions;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DshUeAsset.Core.Fmodel;

/// <summary>
/// Reads %APPDATA%\FModel\AppSettings.json. FModel does not have to be running: this only
/// consumes its saved configuration. AES keys are copied into our own config and never leave it.
/// </summary>
public static class FmodelImporter
{
    public static string DefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "FModel", "AppSettings.json");
    }

    public static ImportResult Import(AppConfig config, string? settingsPath = null)
    {
        var path = string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath() : settingsPath;
        if (!File.Exists(path))
            throw AssetException.NotFound($"FModel settings not found: {path}", new { path });

        var root = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path));
        var perDirectory = root?["PerDirectory"] as JObject;
        if (perDirectory is null || perDirectory.Count == 0)
            throw AssetException.NotFound("FModel settings contain no PerDirectory entries", new { path });

        var imported = new List<string>();
        var skipped = new List<string>();

        foreach (var entry in perDirectory.Properties())
        {
            var value = entry.Value;
            var gameDirectory = value["GameDirectory"]?.Value<string>() ?? entry.Name;
            var paks = ResolvePaksDirectory(gameDirectory);
            if (paks is null)
            {
                skipped.Add($"{gameDirectory}: no .pak/.utoc found under Content/Paks or the directory itself");
                continue;
            }

            var name = value["GameName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name)) name = new DirectoryInfo(paks).Parent?.Parent?.Name ?? "game";
            name = UniqueName(config, name!);

            var profile = new GameProfile
            {
                Name = name,
                Directory = paks,
                Game = MapGame(value["UeVersion"]?.Value<int?>()),
                Platform = MapPlatform(value["TexturePlatform"]?.Value<int?>()),
                ExtraDirectories = ReadExtraDirectories(value)
            };

            var aes = value["AesKeys"];
            var mainKey = aes?["mainKey"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(mainKey) && mainKey.Length > 4) profile.AesKey = mainKey;

            var dynamicKeys = aes?["dynamicKeys"];
            if (dynamicKeys is JArray array)
            {
                foreach (var item in array)
                {
                    var guid = item["guid"]?.Value<string>() ?? item["Guid"]?.Value<string>();
                    var key = item["key"]?.Value<string>() ?? item["Key"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(guid) && !string.IsNullOrWhiteSpace(key))
                        profile.DynamicKeys.Add(new DynamicKey { Guid = guid!, Key = key! });
                }
            }
            else if (dynamicKeys is JObject map)
            {
                foreach (var pair in map.Properties())
                    profile.DynamicKeys.Add(new DynamicKey { Guid = pair.Name, Key = pair.Value.Value<string>() ?? "" });
            }

            config.Profiles[name] = profile;
            imported.Add(name);
        }

        return new ImportResult(imported, skipped, path);
    }

    public static string? ResolvePaksDirectory(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) return null;
        var root = Path.GetFullPath(gameDirectory);
        var paks = Path.Combine(root, "Content", "Paks");
        if (Directory.Exists(paks)) return paks;
        if (Directory.Exists(root) && (Directory.EnumerateFiles(root, "*.pak", SearchOption.TopDirectoryOnly).Any()
                                       || Directory.EnumerateFiles(root, "*.utoc", SearchOption.TopDirectoryOnly).Any()))
            return root;

        // Some FModel entries point one level above the project folder (b1/).
        var childPaks = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, "Content", SearchOption.TopDirectoryOnly)
                .Select(d => Path.Combine(d, "Paks"))
                .FirstOrDefault(Directory.Exists)
            : null;
        return childPaks;
    }

    private static string MapGame(int? ueVersion)
    {
        if (ueVersion is null) return "GAME_UE5_0";

        foreach (var name in Enum.GetNames<EGame>())
        {
            if (Convert.ToInt64(Enum.Parse<EGame>(name)) == ueVersion.Value) return name;
        }

        return ueVersion.Value >= 83886080 ? "GAME_UE5_0" : "GAME_UE4_27";
    }

    private static string MapPlatform(int? platform) => platform switch
    {
        1 => "XboxAndPlaystation4",
        2 => "NintendoSwitch",
        3 => "Playstation5",
        _ => "DesktopMobile"
    };

    private static List<string> ReadExtraDirectories(JToken value)
    {
        var result = new List<string>();
        if (value["Directories"] is not JArray dirs) return result;
        foreach (var dir in dirs)
        {
            var path = dir.Type == JTokenType.String ? dir.Value<string>() : dir["Path"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) result.Add(Path.GetFullPath(path!));
        }
        return result;
    }

    private static string UniqueName(AppConfig config, string name)
    {
        if (!config.Profiles.ContainsKey(name)) return name;
        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{name}-{i}";
            if (!config.Profiles.ContainsKey(candidate)) return candidate;
        }
        return name + "-" + Guid.NewGuid().ToString("N")[..4];
    }
}
