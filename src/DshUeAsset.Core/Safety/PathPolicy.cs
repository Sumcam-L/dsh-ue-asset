namespace DshUeAsset.Core.Safety;

/// <summary>All filesystem writes must stay under the configured export root.</summary>
public static class PathPolicy
{
    public static string EnsureWithin(string root, string candidate)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidateFull = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(candidateFull, rootFull, comparison)) return candidateFull;

        var prefix = rootFull + Path.DirectorySeparatorChar;
        if (!candidateFull.StartsWith(prefix, comparison))
            throw new AssetException(AssetErrorKind.PathDenied, $"path escapes the export root: {candidateFull}", new { root = rootFull });

        return candidateFull;
    }

    public static string NormalizeVirtualPath(string? path)
    {
        var value = (path ?? "").Replace('\\', '/').Trim();
        while (value.StartsWith('/')) value = value[1..];
        return value;
    }

    public static bool IsAssetExtension(string extension)
    {
        return extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".umap", StringComparison.OrdinalIgnoreCase);
    }
}
