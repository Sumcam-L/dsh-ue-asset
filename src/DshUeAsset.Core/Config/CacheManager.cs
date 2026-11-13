namespace DshUeAsset.Core.Config;

/// <summary>One subtree of the cache, so the settings page can explain what the number means.</summary>
public sealed record CachePartView(string Name, string Path, long Bytes, int Files);

public sealed record CacheStatsView(
    string Root,
    long TotalBytes,
    int TotalFiles,
    IReadOnlyList<CachePartView> Parts,
    string? Error);

/// <summary>
/// Measures and clears the internal preview cache. The cache is disposable by construction — GLB
/// files, dependent textures, sampled animation tracks and the SQLite asset indexes are all rebuilt
/// on demand — so clearing it never loses user data.
/// </summary>
public static class CacheManager
{
    public static CacheStatsView Stats(string root)
    {
        var parts = new List<CachePartView>();
        if (!Directory.Exists(root))
            return new CacheStatsView(root, 0, 0, parts, null);

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
                parts.Add(Measure(Path.GetFileName(directory), directory));

            // Loose files directly under the cache root (older layouts kept them here).
            var (looseBytes, looseFiles) = MeasureFiles(root, recurse: false);
            if (looseFiles > 0) parts.Add(new CachePartView("(files)", root, looseBytes, looseFiles));

            return new CacheStatsView(root, parts.Sum(p => p.Bytes), parts.Sum(p => p.Files), parts, null);
        }
        catch (Exception ex)
        {
            return new CacheStatsView(root, 0, 0, parts, ex.Message);
        }
    }

    /// <summary>Deletes the cache tree and recreates an empty root. Returns what was removed.</summary>
    public static CacheStatsView Clear(string root)
    {
        var before = Stats(root);
        if (!Directory.Exists(root)) return before;

        // Per-child deletion so one locked file (a preview being served) cannot abort the rest.
        var errors = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            try
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(Path.GetFileName(entry) + ": " + ex.Message);
            }
        }

        return before with { Error = errors.Count == 0 ? null : string.Join("; ", errors) };
    }

    private static CachePartView Measure(string name, string directory)
    {
        var (bytes, files) = MeasureFiles(directory, recurse: true);
        return new CachePartView(name, directory, bytes, files);
    }

    private static (long Bytes, int Files) MeasureFiles(string directory, bool recurse)
    {
        long bytes = 0;
        var files = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recurse,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    files++;
                }
                catch (IOException)
                {
                    // A file deleted mid-scan is simply not counted.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable subtrees report what was counted so far.
        }

        return (bytes, files);
    }
}
