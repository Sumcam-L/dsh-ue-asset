using System.Text.Json;
using DshUeAsset.Core.Config;

namespace DshUeAsset.Core.Sessions;

/// <summary>
/// Gets an Oodle runtime for the user when none is configured. A local copy always wins (the game's
/// own DLL, an FModel checkout, next to ueasset.exe); only when nothing is found does it download the
/// community-maintained mirror. Oodle itself is proprietary and is never shipped in this repository.
/// </summary>
public static class OodleAcquirer
{
    public const string FileName = "oo2core_9_win64.dll";

    private const string ReleasesApi = "https://api.github.com/repos/new-world-tools/go-oodle/releases?per_page=10";
    // Pinned fallback for when the unauthenticated GitHub API is rate limited.
    private const string FallbackUrl = "https://github.com/new-world-tools/go-oodle/releases/download/v0.2.3-files/oo2core_9_win64.dll";

    public static async Task<(string Path, string Method)> AcquireAsync(
        GameProfile? profile, string? configured, string targetDirectory, CancellationToken cancellationToken = default)
    {
        var local = OodleLocator.Locate(profile ?? new GameProfile { Directory = targetDirectory }, configured);
        if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
            return (local!, "local");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("dsh-ue-asset/1.0");

        var url = await ResolveDownloadUrlAsync(http, cancellationToken).ConfigureAwait(false);
        var bytes = await http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        Validate(bytes);

        return (Write(bytes, targetDirectory), "download");
    }

    private static async Task<string> ResolveDownloadUrlAsync(HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(ReleasesApi, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return FallbackUrl;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("assets", out var assets)) continue;
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var name) || name.GetString() != FileName) continue;
                    if (asset.TryGetProperty("browser_download_url", out var url) &&
                        url.GetString() is { Length: > 0 } found)
                        return found;
                }
            }
        }
        catch
        {
            // Network blocked or API shape changed: the pinned mirror still works.
        }
        return FallbackUrl;
    }

    private static void Validate(byte[] bytes)
    {
        if (bytes.Length is < 100_000 or > 8_000_000)
            throw new AssetException(AssetErrorKind.Internal, "downloaded Oodle has an unexpected size: " + bytes.Length);
        if (bytes.Length < 2 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            throw new AssetException(AssetErrorKind.Internal, "downloaded Oodle is not a Windows DLL");
    }

    private static string Write(byte[] bytes, string preferredDirectory)
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dsh-ue-asset");
        var directories = new List<string> { preferredDirectory };
        if (!directories.Contains(local, StringComparer.OrdinalIgnoreCase)) directories.Add(local);

        foreach (var directory in directories)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, FileName);
                File.WriteAllBytes(path, bytes);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next writable location.
            }
        }

        throw new AssetException(AssetErrorKind.Internal, "could not write " + FileName);
    }
}
