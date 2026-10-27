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

        // Iterate key/value pairs directly: FileProviderDictionary.TryGetValue cannot be trusted
        // to resolve every key it just enumerated (duplicate archive entries / payload keys),
        // which silently dropped every file row and left the tree showing folders only.
        foreach (var pair in provider.Files)
        {
            var key = pair.Key;
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
        foreach (var pair in provider.Files)
        {
            var key = pair.Key;
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