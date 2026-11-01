using DshUeAsset.Core.Config;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using DshUeAsset.Core.Models;
using DshUeAsset.Core.Safety;

namespace DshUeAsset.Core.Assets;

/// <summary>
/// Path-only browsing and search over the provider's in-memory dictionary. This is O(n) per
/// call over provider.Files.Keys, but it is a pure dictionary scan (no I/O) and stays in the
/// millisecond range even for 100k+ virtual paths.
/// </summary>
public static class AssetQuery
{
    public static PageResult<TreeEntry> Browse(IFileProvider provider, string? path, int limit, int offset, GameProfile? profile = null)
    {
        limit = Math.Clamp(limit, 1, 2000);
        offset = Math.Max(0, offset);
        var prefix = NormalizeDirectory(path);
        var dirCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new List<TreeEntry>();
        // FileProviderDictionary enumerates the same virtual path once per archive that ships it
        // (chunk overrides), which used to show duplicate rows for one asset.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Iterate key/value pairs directly: FileProviderDictionary.TryGetValue cannot be trusted
        // to resolve every key it just enumerated (duplicate archive entries / payload keys),
        // which silently dropped every file row and left the tree showing folders only.
        foreach (var pair in provider.Files)
        {
            var key = pair.Key;
            if (!seen.Add(key)) continue;
            if (prefix.Length > 0 && !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = key[prefix.Length..];
            if (rest.Length == 0) continue;

            var slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                var name = rest[..slash];
                if (name.Length == 0) continue;
                dirCounts[name] = dirCounts.GetValueOrDefault(name) + 1;
                continue;
            }

            var extension = Path.GetExtension(rest);
            if (!PathPolicy.IsAssetExtension(extension)) continue;
            files.Add(new TreeEntry(rest, key, false, pair.Value.Size, extension, null, AssetKind.Resolve(provider, profile, key, rest, extension)));
        }

        var entries = new List<TreeEntry>(dirCounts.Count + files.Count);
        foreach (var (name, count) in dirCounts)
            entries.Add(new TreeEntry(name, prefix + name, true, 0, null, count, "folder"));
        entries.AddRange(files);

        entries.Sort(static (a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        var total = entries.Count;
        var page = entries.Skip(offset).Take(limit).ToList();
        var next = offset + page.Count < total ? offset + page.Count : (int?)null;
        return new PageResult<TreeEntry>(page, total, offset, next);
    }

    public static PageResult<TreeEntry> Find(
        IFileProvider provider,
        string? query,
        string? prefix,
        string? extension,
        int limit,
        int offset,
        bool includeNonAssets = false,
        string? kind = null,
        bool nameOnly = false,
        GameProfile? profile = null)
    {
        limit = Math.Clamp(limit, 1, 2000);
        offset = Math.Max(0, offset);
        var q = (query ?? "").Trim();
        var pre = NormalizeDirectory(prefix);
        var ext = string.IsNullOrWhiteSpace(extension) ? null : (extension.StartsWith('.') ? extension : "." + extension);

        var matched = new List<TreeEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in provider.Files)
        {
            var key = pair.Key;
            if (!seen.Add(key)) continue;
            if (pre.Length > 0 && !key.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) continue;

            var fileExt = Path.GetExtension(key);
            if (ext is not null)
            {
                if (!fileExt.Equals(ext, StringComparison.OrdinalIgnoreCase)) continue;
            }
            else if (!includeNonAssets && !PathPolicy.IsAssetExtension(fileExt))
            {
                continue;
            }

            var fileName = Path.GetFileName(key);
            if (q.Length > 0 && (nameOnly ? fileName : key).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var assetKind = AssetKind.Resolve(provider, profile, key, fileName, fileExt);
            if (!string.IsNullOrWhiteSpace(kind) && !assetKind.Equals(kind, StringComparison.OrdinalIgnoreCase)) continue;

            matched.Add(new TreeEntry(fileName, key, false, pair.Value.Size, fileExt, null, assetKind));
            if (matched.Count >= 20_000) break; // hard stop; refine the query instead of paging forever
        }

        matched.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        var total = matched.Count;
        var page = matched.Skip(offset).Take(limit).ToList();
        var next = offset + page.Count < total ? offset + page.Count : (int?)null;
        return new PageResult<TreeEntry>(page, total, offset, next);
    }

    /// <summary>
    /// One pass over the provider restricted to a handful of candidate directories. Used by the
    /// animation picker, which used to call <see cref="Find"/> once per candidate directory and
    /// therefore scanned the whole archive five times.
    /// </summary>
    public static IReadOnlyList<TreeEntry> FindInDirectories(
        IFileProvider provider,
        string? query,
        IReadOnlyList<string> directories,
        string extension,
        int limit,
        string? kind,
        GameProfile? profile)
    {
        limit = Math.Clamp(limit, 1, 2000);
        var q = (query ?? "").Trim();
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var bounds = directories
            .Select(static d => d.Replace('\\', '/').TrimEnd('/'))
            .Where(static d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matched = new List<TreeEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in provider.Files)
        {
            var key = pair.Key;
            if (!seen.Add(key)) continue;
            if (!key.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;

            var inside = false;
            foreach (var bound in bounds)
            {
                if (key.Length > bound.Length
                    && key.StartsWith(bound, StringComparison.OrdinalIgnoreCase)
                    && key[bound.Length] == '/')
                {
                    inside = true;
                    break;
                }
            }
            if (!inside) continue;

            var fileName = Path.GetFileName(key);
            if (q.Length > 0 && fileName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var assetKind = AssetKind.Resolve(provider, profile, key, fileName, ext);
            if (!string.IsNullOrWhiteSpace(kind) && !assetKind.Equals(kind, StringComparison.OrdinalIgnoreCase)) continue;

            matched.Add(new TreeEntry(fileName, key, false, pair.Value.Size, ext, null, assetKind));
            if (matched.Count >= limit * 8) break;
        }

        matched.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return matched.Count > limit ? matched.GetRange(0, limit) : matched;
    }

    public static bool Exists(IFileProvider provider, string virtualPath) => provider.Files.ContainsKey(virtualPath);

    public static GameFile? FindFile(IFileProvider provider, string virtualPath)
    {
        foreach (var candidate in Candidates(virtualPath))
        {
            if (provider.Files.TryGetValue(candidate, out var file)) return file;
        }
        foreach (var pair in provider.Files)
        {
            if (pair.Key.Equals(virtualPath, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    }

    /// <summary>
    /// The project's mount point, e.g. "b1" for "b1/Content/...". Discovered from the file list
    /// because a cooked archive mounts "/Game" at "&lt;Project&gt;/Content" and the name is not recorded.
    /// </summary>
    public static string? ProjectContentRoot(IFileProvider provider)
    {
        foreach (var key in provider.Files.Keys)
        {
            if (key.StartsWith("Engine/", StringComparison.OrdinalIgnoreCase)) continue;
            var index = key.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
            if (index <= 0) continue;
            var root = key[..index];
            if (root.Contains('/')) continue;
            return root;
        }
        return null;
    }

    /// <summary>
    /// Turns a UE object path ("/Game/A/B", "/Engine/A/B") into a provider virtual path so an
    /// import can be opened straight from a card. Package import entries carry these API paths.
    /// </summary>
    public static string? VirtualizeObjectPath(string? objectPath, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(objectPath)) return null;
        var path = objectPath.Trim();
        if (!path.StartsWith('/')) return path;

        var rest = path[1..];
        var slash = rest.IndexOf('/');
        var root = slash < 0 ? rest : rest[..slash];
        var tail = slash < 0 ? string.Empty : rest[(slash + 1)..];

        if (root.Equals("Engine", StringComparison.OrdinalIgnoreCase)) return "Engine/Content/" + tail;
        if (!root.Equals("Game", StringComparison.OrdinalIgnoreCase)) return null;
        return projectRoot is null ? null : projectRoot + "/Content/" + tail;
    }

    /// <summary>Resolves "Foo" to "Foo.uasset" / "Foo.umap" when the caller omitted the extension.</summary>
    public static string ResolveVirtualPath(IFileProvider provider, string virtualPath)
    {
        foreach (var candidate in Candidates(virtualPath))
        {
            if (provider.Files.ContainsKey(candidate)) return candidate;
        }
        foreach (var pair in provider.Files)
        {
            if (pair.Key.Equals(virtualPath, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }
        return virtualPath;
    }

    private static IEnumerable<string> Candidates(string virtualPath)
    {
        yield return virtualPath;
        var extension = Path.GetExtension(virtualPath);
        if (!PathPolicy.IsAssetExtension(extension))
        {
            yield return virtualPath + ".uasset";
            yield return virtualPath + ".umap";
        }
    }

    private static string NormalizeDirectory(string? path)
    {
        var value = PathPolicy.NormalizeVirtualPath(path);
        if (value.Length == 0) return "";
        return value.EndsWith('/') ? value : value + "/";
    }
}