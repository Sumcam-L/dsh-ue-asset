using CUE4Parse.UE4.Versions;

namespace DshUeAsset.Core.Config;

/// <summary>One selectable value of the CUE4Parse <c>EGame</c> enum, exposed to the settings UI.</summary>
public sealed record GameOption(string Name, string Label, string Group);

/// <summary>
/// Reflects <see cref="EGame"/> so the settings page never hard-codes a version list: the parser's own
/// enum is the single source of truth for the engine versions and game profiles it understands.
/// </summary>
public static class GameCatalog
{
    private static readonly Lazy<IReadOnlyList<GameOption>> Cached =
        new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<GameOption> All() => Cached.Value;

    private static IReadOnlyList<GameOption> Build()
    {
        var options = new List<GameOption>();
        foreach (var name in Enum.GetNames<EGame>())
        {
            if (name is "GAME_Undefined" or "GAME_Custom") continue;
            options.Add(new GameOption(name, Label(name), Group(name)));
        }
        // Base engine versions first (UE4, UE5), then game-specific profiles; stable inside a group.
        return options
            .OrderBy(option => option.Group switch { "UE4" => 0, "UE5" => 1, _ => 2 })
            .ToList();
    }

    private static string Group(string name)
    {
        if (name.StartsWith("GAME_UE4_", StringComparison.Ordinal)) return "UE4";
        if (name.StartsWith("GAME_UE5_", StringComparison.Ordinal)) return "UE5";
        return "Games";
    }

    private static string Label(string name)
    {
        var trimmed = name.StartsWith("GAME_", StringComparison.Ordinal) ? name[5..] : name;
        if (trimmed is "UE4_LATEST") return "UE4 (latest)";
        if (trimmed is "UE5_LATEST") return "UE5 (latest)";
        // GAME_UE5_3 -> UE5.3, GAME_UE4_27 -> UE4.27, GAME_UE5_EA -> UE5 EA
        if (trimmed.Length > 4 &&
            (trimmed.StartsWith("UE4_", StringComparison.Ordinal) || trimmed.StartsWith("UE5_", StringComparison.Ordinal)))
        {
            var family = trimmed[..3];
            var suffix = trimmed[4..];
            return suffix.All(char.IsAsciiDigit) ? family + "." + suffix : family + " " + suffix;
        }
        return trimmed;
    }
}
