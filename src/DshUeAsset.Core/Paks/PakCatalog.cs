using System.Text.RegularExpressions;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Models;

namespace DshUeAsset.Core.Paks;

public static class PakCatalog
{
    private static readonly Regex ChunkPattern = new(@"(?:pak)?chunk[_-]?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<PakInfo> List(GameProfile profile)
    {
        var directories = new List<string> { profile.Directory };
        directories.AddRange(profile.ExtraDirectories.Where(Directory.Exists));

        var result = new List<PakInfo>();
        foreach (var directory in directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = System.IO.Path.GetExtension(path);
                if (!extension.Equals(".pak", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase)) continue;

                var name = System.IO.Path.GetFileName(path);
                var tags = profile.PakTags
                    .FirstOrDefault(t => t.File.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?.Tags ?? [];
                var match = ChunkPattern.Match(name);
                var chunk = match.Success ? int.Parse(match.Groups[1].Value) : 0;
                result.Add(new PakInfo(
                    name,
                    path,
                    new FileInfo(path).Length,
                    chunk,
                    extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase),
                    tags));
            }
        }

        return result.OrderBy(p => p.Chunk).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Resolve the caller's pak names / tags into concrete files. Names may omit ".pak" and
    /// are matched case-insensitively. Tags match if any requested tag is present.
    /// </summary>
    public static IReadOnlyList<PakInfo> Resolve(GameProfile profile, IReadOnlyList<string>? names, IReadOnlyList<string>? tags)
    {
        var all = List(profile);
        var wantedNames = (names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        var wantedTags = (tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();

        if (wantedNames.Count == 0 && wantedTags.Count == 0)
        {
            wantedNames = profile.DefaultPaks.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (wantedNames.Count == 0)
                throw new AssetException(AssetErrorKind.NeedsPaks,
                    $"profile '{profile.Name}' has no default paks; choose paks or tags",
                    new { profile = profile.Name, available = all });
        }

        var selected = new List<PakInfo>();
        foreach (var wanted in wantedNames)
        {
            var match = all.FirstOrDefault(p =>
                p.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                System.IO.Path.GetFileNameWithoutExtension(p.Name).Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw AssetException.Invalid($"pak not found: {wanted}", new { pak = wanted, available = all.Select(p => p.Name) });
            if (match.IsUtoc)
                throw AssetException.Unsupported($"IoStore (.utoc) selection is not supported yet: {match.Name}", new { pak = match.Name });
            if (!selected.Contains(match)) selected.Add(match);
        }

        foreach (var tag in wantedTags)
        {
            foreach (var pak in all.Where(p => p.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            {
                if (pak.IsUtoc)
                    throw AssetException.Unsupported($"IoStore (.utoc) selection is not supported yet: {pak.Name}", new { pak = pak.Name });
                if (!selected.Contains(pak)) selected.Add(pak);
            }
        }

        if (selected.Count == 0)
            throw AssetException.Invalid("pak selection is empty", new { names = wantedNames, tags = wantedTags });

        // Engine defaults and plugin content (ACL codecs) live in the base chunk. Without it,
        // animation decompression loses its codec settings even when the clip itself is mounted.
        if (!selected.Any(p => p.Chunk == 0))
        {
            var basePak = all.Where(p => p.Chunk == 0).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (basePak is not null) selected.Add(basePak);
        }

        return selected.OrderBy(p => p.Chunk).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static PakCatalogView Catalog(GameProfile profile) =>
        new(profile.Name, List(profile), [.. profile.DefaultPaks]);

    public static void SetTags(GameProfile profile, string pakName, IReadOnlyList<string> tags)
    {
        var entry = profile.PakTags.FirstOrDefault(t => t.File.Equals(pakName, StringComparison.OrdinalIgnoreCase));
        var cleaned = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (entry is null)
        {
            profile.PakTags.Add(new PakTag { File = pakName, Tags = cleaned });
            return;
        }
        entry.Tags = cleaned;
    }
}