using Newtonsoft.Json;

namespace DshUeAsset.Core.Config;

public sealed class AppConfig
{
    public Dictionary<string, GameProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OutputRoot { get; set; }
    /// <summary>Preview cache + asset index root. Null means %LOCALAPPDATA%/dsh-ue-asset/cache.</summary>
    public string? CacheRoot { get; set; }
    public string? OodleLibrary { get; set; }
    public string DefaultMeshFormat { get; set; } = "UEFormat";
    public Limits Limits { get; set; } = new();
    public UiState UiState { get; set; } = new();

    public static string DefaultConfigPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "dsh-ue-asset", "config.json");
    }

    public string ResolveCacheRoot()
    {
        if (!string.IsNullOrWhiteSpace(CacheRoot)) return CacheRoot!;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "dsh-ue-asset", "cache");
    }

    public static string DefaultOutputRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "dsh-ue-asset", "Exports");
    }
}

public sealed class GameProfile
{
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
    public List<string> ExtraDirectories { get; set; } = new();
    public string Game { get; set; } = "GAME_UE5_0";
    public string Platform { get; set; } = "DesktopMobile";
    public string? AesKey { get; set; }
    public List<DynamicKey> DynamicKeys { get; set; } = new();
    public string? Usmap { get; set; }
    /// <summary>Per-pak tags, editable in Settings. Pak names are meaningless; tags drive selection.</summary>
    public List<PakTag> PakTags { get; set; } = new();
    /// <summary>Paks mounted when a caller does not specify a selection. Empty means "ask".</summary>
    public List<string> DefaultPaks { get; set; } = new();
    /// <summary>Per-game filename prefix → kind. Overrides built-in conventions when the registry has no entry.</summary>
    public List<PrefixRule> PrefixRules { get; set; } = new();

    [JsonIgnore]
    public string Key => NormalizeDirectory(Directory);

    public GameProfile Clone() => new()
    {
        Name = Name,
        Directory = Directory,
        ExtraDirectories = [.. ExtraDirectories],
        Game = Game,
        Platform = Platform,
        AesKey = AesKey,
        DynamicKeys = [.. DynamicKeys],
        Usmap = Usmap,
        PakTags = PakTags.Select(t => new PakTag { File = t.File, Tags = [.. t.Tags] }).ToList(),
        DefaultPaks = [.. DefaultPaks],
        PrefixRules = PrefixRules.Select(r => new PrefixRule { Prefix = r.Prefix, Kind = r.Kind }).ToList()
    };

    public static string NormalizeDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return "";
        var full = Path.GetFullPath(directory.Replace('/', Path.DirectorySeparatorChar));
        return Path.TrimEndingDirectorySeparator(full);
    }
}

public sealed class PrefixRule
{
    public string Prefix { get; set; } = "";
    public string Kind { get; set; } = "asset";
}

public sealed class PakTag
{
    public string File { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

public sealed class DynamicKey
{
    public string Guid { get; set; } = "";
    public string Key { get; set; } = "";
}

public sealed class Limits
{
    public int MaxSessions { get; set; } = 4;
    public int MaxJobs { get; set; } = 16;
    public int MaxBatchAssets { get; set; } = 200;
    public long MaxReadBytes { get; set; } = 64 * 1024 * 1024;
    public int MaxResponseChars { get; set; } = 80_000;
}

public sealed class UiState
{
    public string? LastProfile { get; set; }
    public List<string> ExpandedPaths { get; set; } = new();
}
