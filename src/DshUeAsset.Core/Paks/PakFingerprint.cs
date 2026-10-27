using DshUeAsset.Core.Config;

namespace DshUeAsset.Core.Paks;

/// <summary>
/// Stable signature of the pak files an index was built from: name + size + last write time.
/// Comparing it against the filesystem tells us whether an index is stale.
/// </summary>
public static class PakFingerprint
{
    public static string Compute(GameProfile profile, IEnumerable<string> paks)
    {
        var files = PakCatalog.List(profile)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var parts = new List<string>();
        foreach (var name in paks.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!files.TryGetValue(name, out var pak))
            {
                parts.Add(name + ":missing");
                continue;
            }

            try
            {
                var info = new FileInfo(pak.Path);
                parts.Add(name + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks);
            }
            catch
            {
                parts.Add(name + ":unreadable");
            }
        }

        return string.Join(';', parts);
    }

    public static (bool Stale, string? Reason) Compare(GameProfile profile, IReadOnlyList<string> builtPaks, string? builtFingerprint)
    {
        if (builtPaks.Count == 0)
            return (false, null);

        if (string.IsNullOrWhiteSpace(builtFingerprint))
            return (true, "index has no fingerprint (built by an older version) — rebuild recommended");

        var current = Compute(profile, builtPaks);
        if (current == builtFingerprint) return (false, null);

        var reasons = new List<string>();
        var built = builtFingerprint.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var now = current.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < Math.Max(built.Length, now.Length); i++)
        {
            var before = i < built.Length ? built[i] : "(none)";
            var after = i < now.Length ? now[i] : "(none)";
            if (before == after) continue;
            var name = (after ?? before).Split(':')[0];
            reasons.Add(after.EndsWith(":missing", StringComparison.Ordinal) ? $"pak missing: {name}" : $"pak changed: {name}");
        }

        var reason = reasons.Count > 0 ? string.Join("; ", reasons.Distinct()) : "pak set changed";
        return (true, reason);
    }
}
