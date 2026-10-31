using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Models;

namespace DshUeAsset.Core.Sessions;

/// <summary>
/// The only place that knows how CUE4Parse is wired together.
/// Verified against CUE4Parse 1.2.2.202610: the current DefaultFileProvider overload is
/// (directory, extraDirectories, searchOption, versions, pathComparer); the legacy
/// isCaseInsensitive overload is [Obsolete].
/// </summary>
public static class ProviderFactory
{
    private static readonly object OodleLock = new();
    private static string? _loadedOodle;

    public static (IFileProvider Provider, MountReport Report) Create(GameProfile profile, string? oodleLibrary, IReadOnlyList<PakInfo>? selection = null)
    {
        if (string.IsNullOrWhiteSpace(profile.Directory))
            throw AssetException.Invalid("profile.directory is empty");
        if (!Directory.Exists(profile.Directory))
            throw AssetException.NotFound($"paks directory does not exist: {profile.Directory}", new { profile.Directory });

        var warnings = new List<string>();
        InitializeOodle(profile, oodleLibrary, warnings);

        var game = ParseEnum<EGame>(profile.Game, "game");
        var platform = ParseEnum<ETexturePlatform>(profile.Platform, "platform");
        var versions = new VersionContainer(game: game, platform: platform);

        var extras = profile.ExtraDirectories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => new DirectoryInfo(d))
            .Where(d => d.Exists)
            .ToArray();

        var provider = new DefaultFileProvider(
            new DirectoryInfo(profile.Directory),
            extras,
            SearchOption.TopDirectoryOnly,
            versions,
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(profile.Usmap))
        {
            if (File.Exists(profile.Usmap))
                provider.MappingsContainer = new FileUsmapTypeMappingsProvider(profile.Usmap);
            else
                warnings.Add($"usmap not found: {profile.Usmap}");
        }

        if (selection is { Count: > 0 })
        {
            // Selective mount: do NOT call Initialize(), which would scan and register every
            // archive in the directory. Register only the caller's paks.
            foreach (var pak in selection)
            {
                if (!File.Exists(pak.Path))
                    throw AssetException.NotFound($"pak file missing: {pak.Path}", new { pak = pak.Name });
                provider.RegisterVfs(pak.Path);
            }
            warnings.Add($"selective mount: {selection.Count} pak(s)");
        }
        else
        {
            provider.Initialize();
        }

        var submitted = 0;
        if (!string.IsNullOrWhiteSpace(profile.AesKey))
        {
            submitted += provider.SubmitKey(new FGuid(), new FAesKey(CleanKey(profile.AesKey)));
        }

        foreach (var key in profile.DynamicKeys)
        {
            if (string.IsNullOrWhiteSpace(key.Guid) || string.IsNullOrWhiteSpace(key.Key)) continue;
            try
            {
                submitted += provider.SubmitKey(new FGuid(key.Guid.Trim()), new FAesKey(CleanKey(key.Key)));
            }
            catch (Exception ex)
            {
                warnings.Add($"dynamic key rejected for {key.Guid}: {ex.Message}");
            }
        }

        var missing = provider is AbstractVfsFileProvider vfs
            ? vfs.RequiredKeys.Distinct().Select(g => g.ToString()).ToArray()
            : [];

        // SubmitKey itself mounts every reader whose GUID it covers, so Mount() returning 0
        // is normal on the happy path. The real success signal is MountedVfs.
        var newMounts = provider.Mount();
        provider.PostMount();
        RegisterPluginVirtualPaths(provider);
        var mounted = provider.MountedVfs.Count;

        // Missing keys never abort a mount that produced usable archives: the panel shows a
        // closable needs-key banner while the mounted part stays browsable (UI-DESIGN §6).
        if (mounted == 0)
        {
            provider.Dispose();
            var selected = selection is { Count: > 0 };
            var detail = new
            {
                mounted,
                newMounts,
                missingKeys = missing,
                directory = profile.Directory,
                game = profile.Game,
                paks = selected ? selection!.Select(p => p.Name).ToArray() : null
            };
            // A registered pak that mounts nothing is almost always a wrong version container, not a
            // missing file: CUE4Parse drops the reader silently when the EGame does not match the pak.
            throw new AssetException(AssetErrorKind.NeedsKey,
                missing.Length > 0
                    ? $"no archive could be mounted; missing AES keys for {missing.Length} GUID(s)"
                    : selected
                        ? $"none of the {selection!.Count} selected pak(s) could be mounted under engine container '{profile.Game}'; "
                          + "a mismatched EGame makes the pak unreadable — pass the game-specific one (e.g. GAME_BlackMythWukong)"
                        : "no archive could be mounted from the configured directory (no .pak/.utoc found)",
                detail);
        }

        if (missing.Length > 0)
            warnings.Add($"missing AES keys for {missing.Length} GUID(s); encrypted entries stay unreadable");

        var report = new MountReport(
            Mounted: mounted,
            Submitted: submitted,
            MissingKeys: missing,
            FileCount: provider.Files.Count,
            EngineVersion: $"{provider.Versions.Game} / {provider.Versions.Ver}",
            Warning: warnings.Count == 0 ? null : string.Join("; ", warnings));

        return (provider, report);
    }

    /// <summary>
    /// Plugin packages are referenced by their mount root (/ACLPlugin/...), while the virtual
    /// files live under <project>/Plugins/ACLPlugin/Content/... . CUE4Parse's FixPath consults
    /// VirtualPaths for unknown roots, so register one mapping per plugin.
    /// </summary>
    private static void RegisterPluginVirtualPaths(IFileProvider provider)
    {
        if (provider is not AbstractFileProvider abstractProvider) return;
        foreach (var key in provider.Files.Keys)
        {
            var marker = "/Plugins/";
            var index = key.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index <= 0) continue;
            var rest = key[(index + marker.Length)..];
            var plugin = rest.Split('/')[0];
            if (string.IsNullOrWhiteSpace(plugin)) continue;
            abstractProvider.VirtualPaths[plugin] = key[..index] + "/Plugins/" + plugin;
        }
    }

    private static void InitializeOodle(GameProfile profile, string? configured, List<string> warnings)
    {
        lock (OodleLock)
        {
            if (!string.IsNullOrWhiteSpace(_loadedOodle)) return;

            var path = OodleLocator.Locate(profile, configured);
            if (string.IsNullOrWhiteSpace(path))
            {
                warnings.Add("oodle runtime not found: put oo2core_9_win64.dll next to ueasset.exe, set OODLE_DLL, or configure oodleLibrary");
                return;
            }

            try
            {
                // The documented runtime downloads Oodle at startup. This tool never reaches the
                // network: an explicit local DLL is required.
                OodleHelper.Initialize(path);
                _loadedOodle = path;
                if (!path.Equals(configured, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"oodle auto-detected: {path}");
            }
            catch (Exception ex)
            {
                warnings.Add($"oodle initialization failed: {ex.Message}");
            }
        }
    }

    private static T ParseEnum<T>(string value, string field) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
        {
            var hint = typeof(T) == typeof(EGame) ? " e.g. GAME_BlackMythWukong / GAME_UE5_0" : "";
            throw AssetException.Invalid($"unknown {field} value '{value}'.{hint}", new { field, value });
        }

        return parsed;
    }

    private static string CleanKey(string key)
    {
        var value = key.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
    }
}

public sealed record MountReport(
    int Mounted,
    int Submitted,
    IReadOnlyList<string> MissingKeys,
    long FileCount,
    string? EngineVersion,
    string? Warning)
{
    public bool NeedsKey => MissingKeys.Count > 0;
}