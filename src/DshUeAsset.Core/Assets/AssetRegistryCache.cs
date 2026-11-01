using System.Collections.Concurrent;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.AssetRegistry;

namespace DshUeAsset.Core.Assets;

/// <summary>
/// Lazily parses AssetRegistry.bin once per provider. The registry gives the real class of
/// every cooked asset without loading each package, which is the authoritative type source.
/// </summary>
public static class AssetRegistryCache
{
    public sealed record Snapshot(bool Loaded, int Count, string? Error, DateTimeOffset? LoadedAt);

    private sealed class Entry
    {
        // Sized by the caller once the registry length is known: 677k inserts into a growing
        // dictionary otherwise rehash a dozen times.
        public Dictionary<string, string> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Snapshot Snapshot { get; set; } = new(false, 0, null, null);
    }

    // Lazy, not a plain dictionary: publishing an empty entry first made every concurrent caller
    // see "loaded, but no such asset" while the 340k-entry registry was still parsing, so all kind
    // resolution silently fell back to filename prefixes.
    private static readonly ConcurrentDictionary<IFileProvider, Lazy<Entry>> Cache = new();

    public static Snapshot GetStatus(IFileProvider provider) => Get(provider).Snapshot;

    public static string? GetClass(IFileProvider provider, string path)
    {
        var key = Normalize(path);
        return Get(provider).Classes.TryGetValue(key, out var value) ? value : null;
    }

    private static Entry Get(IFileProvider provider) =>
        Cache.GetOrAdd(provider, p => new Lazy<Entry>(() => Load(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static Entry Load(IFileProvider provider)
    {
        var entry = new Entry();
        try
        {
            var (contentPrefix, registryPath) = ScanFiles(provider);
            if (registryPath is null)
            {
                entry.Snapshot = new Snapshot(false, 0, "AssetRegistry.bin not found", null);
                return entry;
            }

            if (!provider.TryCreateReader(registryPath, out var reader))
                throw new InvalidOperationException("cannot open " + registryPath);

            using (reader)
            {
                var state = new FAssetRegistryState(reader);
                var assets = state.PreallocatedAssetDataBuffers;
                entry.Classes = new Dictionary<string, string>(Math.Max(1024, assets.Length * 2), StringComparer.OrdinalIgnoreCase);
                foreach (var asset in assets)
                {
                    var package = asset.PackageName.Text;
                    if (string.IsNullOrWhiteSpace(package)) continue;
                    var className = asset.AssetClass.Text;
                    var assetName = asset.AssetName.Text;

                    // Only the virtual form is ever queried (AssetKind.Resolve passes provider
                    // paths), so the raw "/Game/..." keys are not stored: that halves the table.
                    if (contentPrefix is not null && package.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
                    {
                        var virtualPath = contentPrefix + package[6..];
                        entry.Classes[virtualPath] = className;
                        entry.Classes[virtualPath + "." + assetName] = className;
                    }
                    else
                    {
                        entry.Classes[package] = className;
                        entry.Classes[package + "." + assetName] = className;
                    }
                }
            }

            entry.Snapshot = new Snapshot(true, entry.Classes.Count, null, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            entry.Snapshot = new Snapshot(false, 0, ex.Message, null);
        }

        return entry;
    }

    /// <summary>
    /// One pass over the file list yields both the registry path and the project mount point; the
    /// provider holds half a million keys and scanning them twice was measurable.
    /// </summary>
    private static (string? ContentPrefix, string? RegistryPath) ScanFiles(IFileProvider provider)
    {
        string? contentPrefix = null;
        string? registryPath = null;
        var scanned = 0;
        foreach (var path in provider.Files.Keys)
        {
            if (registryPath is null && path.EndsWith("AssetRegistry.bin", StringComparison.OrdinalIgnoreCase))
            {
                registryPath = path;
                if (contentPrefix is not null) break;
            }

            if (contentPrefix is null)
            {
                var index = path.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
                if (index > 0) contentPrefix = path[..(index + "/Content/".Length)];
            }

            if (contentPrefix is not null && registryPath is not null) break;
            if (++scanned > 200_000) break;
        }

        return (contentPrefix, registryPath);
    }

    private static string Normalize(string path)
    {
        var value = path.IndexOf('\\') >= 0 ? path.Replace('\\', '/') : path;
        if (value.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) value = value[..^7];
        return value;
    }
}