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
        public Dictionary<string, string> Classes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Snapshot Snapshot { get; set; } = new(false, 0, null, null);
    }

    private static readonly ConcurrentDictionary<IFileProvider, Entry> Cache = new();

    public static Snapshot GetStatus(IFileProvider provider)
    {
        Ensure(provider);
        return Cache.TryGetValue(provider, out var entry) ? entry.Snapshot : new Snapshot(false, 0, "not initialized", null);
    }

    public static string? GetClass(IFileProvider provider, string path)
    {
        Ensure(provider);
        if (!Cache.TryGetValue(provider, out var entry)) return null;
        var key = Normalize(path);
        return entry.Classes.TryGetValue(key, out var value) ? value : null;
    }

    private static void Ensure(IFileProvider provider)
    {
        if (Cache.ContainsKey(provider)) return;
        var entry = new Entry();
        Cache[provider] = entry;
        lock (entry)
        {
            try
            {
                var registryPath = FindRegistry(provider);
                if (registryPath is null)
                {
                    entry.Snapshot = new Snapshot(false, 0, "AssetRegistry.bin not found", null);
                    return;
                }

                if (!provider.TryCreateReader(registryPath, out var reader))
                    throw new InvalidOperationException("cannot open " + registryPath);

                using (reader)
                {
                    var state = new FAssetRegistryState(reader);
                    var contentPrefix = DetectContentPrefix(provider);
                    foreach (var asset in state.PreallocatedAssetDataBuffers)
                    {
                        var package = asset.PackageName.Text;
                        if (string.IsNullOrWhiteSpace(package)) continue;
                        var className = asset.AssetClass.Text;
                        entry.Classes[Normalize(package)] = className;
                        entry.Classes[Normalize(package + "." + asset.AssetName.Text)] = className;

                        // Registry paths are mount-point relative (/Game/...); the provider uses
                        // virtual paths (b1/Content/...). Store both forms.
                        if (contentPrefix is not null && package.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
                        {
                            var virtualPath = contentPrefix + package[6..];
                            entry.Classes[Normalize(virtualPath)] = className;
                            entry.Classes[Normalize(virtualPath + "." + asset.AssetName.Text)] = className;
                        }
                    }
                }

                entry.Snapshot = new Snapshot(true, entry.Classes.Count, null, DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                entry.Snapshot = new Snapshot(false, 0, ex.Message, null);
            }
        }
    }

    private static string? FindRegistry(IFileProvider provider)
    {
        foreach (var path in provider.Files.Keys)
        {
            if (path.EndsWith("AssetRegistry.bin", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    private static string? DetectContentPrefix(IFileProvider provider)
    {
        var scanned = 0;
        foreach (var path in provider.Files.Keys)
        {
            var index = path.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return path[..(index + "/Content/".Length)];
            }
            if (++scanned > 5000) break;
        }
        return null;
    }

    private static string Normalize(string path)
    {
        var value = path.Replace('\\', '/').TrimEnd();
        if (value.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) value = value[..^7];
        return value;
    }
}