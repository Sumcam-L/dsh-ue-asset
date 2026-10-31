using System.Collections.Concurrent;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Objects.Core.Misc;
using DshUeAsset.Core.Assets;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Export;
using DshUeAsset.Core.Fmodel;
using DshUeAsset.Core.Models;
using DshUeAsset.Core.Safety;
using DshUeAsset.Core.Sessions;
using DshUeAsset.Core.Index;
using Newtonsoft.Json.Linq;

namespace DshUeAsset.Core.Services;

public sealed record OpenRequest(string? Profile, string? Directory, string? Game, string? AesKey, string? Usmap, IReadOnlyList<string>? Paks = null, IReadOnlyList<string>? Tags = null);

/// <summary>Result of attaching one AES key to one pak (see AssetService.BindPakKey).</summary>
public sealed record PakKeyBinding(bool Bound, string Pak, string? KeyGuid, bool Mounted, int NewMounts, long FileCount, string? Reason);

/// <summary>Transport-neutral façade shared by the CLI and the JSON-lines server.</summary>
public sealed class AssetService : IDisposable
{
    private readonly ConfigStore _store;
    private readonly object _configLock = new();
    // Animation discovery is a full archive scan per mesh; the sidebar asks again on every reopen.
    private readonly ConcurrentDictionary<string, IReadOnlyList<TreeEntry>> _animationCache = new(StringComparer.OrdinalIgnoreCase);

    public AssetService(ConfigStore? store = null)
    {
        _store = store ?? new ConfigStore();
        Config = _store.Load();
        Sessions = new SessionManager(Config);
    }

    public ConfigStore Store => _store;

    /// <summary>Internal, disposable preview cache (GLB + dependent textures). Never user-facing.</summary>
    public string CacheRoot
    {
        get
        {
            var root = Config.ResolveCacheRoot();
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public string IndexRoot
    {
        get
        {
            var root = Path.Combine(CacheRoot, "index");
            Directory.CreateDirectory(root);
            return root;
        }
    }
    public AppConfig Config { get; private set; }
    public SessionManager Sessions { get; }

    public PakCatalogView ListPaks(string profileName)
    {
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profileName, out var profile))
                throw AssetException.NotFound($"profile not found: {profileName}");
            return DshUeAsset.Core.Paks.PakCatalog.Catalog(profile);
        }
    }

    public PakCatalogView SetPakTags(string profileName, string pakName, IReadOnlyList<string> tags)
    {
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profileName, out var profile))
                throw AssetException.NotFound($"profile not found: {profileName}");
            DshUeAsset.Core.Paks.PakCatalog.SetTags(profile, pakName, tags);
            SaveConfigLocked();
            return DshUeAsset.Core.Paks.PakCatalog.Catalog(profile);
        }
    }

    public PakCatalogView GetPakTags(string profileName)
    {
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profileName, out var profile))
                throw AssetException.NotFound($"profile not found: {profileName}");
            return DshUeAsset.Core.Paks.PakCatalog.Catalog(profile);
        }
    }

    public IReadOnlyList<SessionSummary> ListSessions() => Sessions.All.Select(s => s.ToSummary()).ToList();

    /// <summary>
    /// Per-pak AES state for the pak picker. A live session answers without touching disk;
    /// otherwise a throwaway provider registers the readers only (footer + index header, no mount).
    /// </summary>
    public IReadOnlyList<PakKeyState> ListPakKeys(string profileName)
    {
        GameProfile profile;
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profileName, out var stored))
                throw AssetException.NotFound($"profile not found: {profileName}");
            profile = stored;
        }

        var live = Sessions.All.FirstOrDefault(s => s.Profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (live?.Provider is AbstractVfsFileProvider mounted)
        {
            lock (_configLock) return PakKeys.Describe(mounted, guid => HasConfiguredKey(profile, guid));
        }

        var warnings = new List<string>();
        using var provider = ProviderFactory.CreateProvider(profile, Config.OodleLibrary, warnings);
        foreach (var pak in DshUeAsset.Core.Paks.PakCatalog.List(profile))
        {
            try { provider.RegisterVfs(pak.Path); }
            catch { /* an unreadable pak simply stays out of the list */ }
        }

        lock (_configLock) return PakKeys.Describe(provider, guid => HasConfiguredKey(profile, guid));
    }

    /// <summary>
    /// Bind one AES key to one pak. The key is validated against that pak's index header first,
    /// so a wrong key is reported instead of being stored. When the profile is already open the
    /// pak mounts immediately and the session summary is refreshed.
    /// </summary>
    public PakKeyBinding BindPakKey(string profileName, string pakName, string? key)
    {
        if (string.IsNullOrWhiteSpace(pakName)) throw AssetException.Invalid("pak name is empty");
        var clean = ProviderFactory.CleanKey(key ?? string.Empty);
        if (clean.Length == 0) throw AssetException.Invalid("aes key is empty");

        GameProfile profile;
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profileName, out var stored))
                throw AssetException.NotFound($"profile not found: {profileName}");
            profile = stored;
        }

        var live = Sessions.All.FirstOrDefault(s => s.Profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (live?.Provider is AbstractVfsFileProvider mounted)
            return BindOnProvider(profile, live, mounted, pakName, clean);

        var warnings = new List<string>();
        using var provider = ProviderFactory.CreateProvider(profile, Config.OodleLibrary, warnings);
        var info = DshUeAsset.Core.Paks.PakCatalog.List(profile)
            .FirstOrDefault(p => p.Name.Equals(pakName, StringComparison.OrdinalIgnoreCase))
            ?? throw AssetException.NotFound($"pak not found in profile '{profileName}': {pakName}");
        provider.RegisterVfs(info.Path);
        return BindOnProvider(profile, null, provider, pakName, clean);
    }

    private PakKeyBinding BindOnProvider(GameProfile profile, GameSession? session, AbstractVfsFileProvider provider, string pakName, string cleanKey)
    {
        var reader = PakKeys.Find(provider, pakName)
            ?? throw AssetException.NotFound($"pak is not registered: {pakName}");
        if (!reader.IsEncrypted)
            return new PakKeyBinding(false, reader.Name, null, true, 0, provider.Files.Count, "this pak is not encrypted");

        var guid = reader.EncryptionKeyGuid;
        if (!PakKeys.AcceptsKey(reader, cleanKey))
            return new PakKeyBinding(false, reader.Name, guid.ToString(), false, 0, provider.Files.Count,
                "the key was rejected by this pak (its index header does not decrypt)");

        lock (_configLock)
        {
            var text = guid.ToString();
            var entry = profile.DynamicKeys.FirstOrDefault(k => string.Equals(k.Guid, text, StringComparison.OrdinalIgnoreCase));
            if (entry is null) profile.DynamicKeys.Add(new DynamicKey { Guid = text, Key = cleanKey });
            else entry.Key = cleanKey;
            SaveConfigLocked();
        }

        var submitted = provider.SubmitKey(guid, new FAesKey(cleanKey));
        ProviderFactory.AfterMount(provider);
        var mounted = PakKeys.IsMounted(provider, reader);
        if (session is not null) session.UpdateReport(BuildMountReport(provider, session.Report));

        return new PakKeyBinding(true, reader.Name, guid.ToString(), mounted, submitted, provider.Files.Count, null);
    }

    private static MountReport BuildMountReport(AbstractVfsFileProvider provider, MountReport previous)
    {
        var missing = provider.RequiredKeys.Select(guid => guid.ToString()).ToArray();
        return new MountReport(
            provider.MountedVfs.Count,
            previous.Submitted + 1,
            missing,
            provider.Files.Count,
            previous.EngineVersion,
            missing.Length == 0 ? null : previous.Warning);
    }

    private static bool HasConfiguredKey(GameProfile profile, FGuid guid)
    {
        if (guid == default) return !string.IsNullOrWhiteSpace(profile.AesKey);
        var text = guid.ToString();
        return profile.DynamicKeys.Any(k => string.Equals(k.Guid, text, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SessionSummary> OpenAsync(OpenRequest request, CancellationToken cancellationToken = default)
    {
        GameProfile profile;
        lock (_configLock)
        {
            profile = ResolveProfile(request);
        }

        IReadOnlyList<DshUeAsset.Core.Models.PakInfo> selection;
        lock (_configLock)
        {
            selection = DshUeAsset.Core.Paks.PakCatalog.Resolve(profile, request.Paks, request.Tags);
        }

        var session = await Task.Run(() => Sessions.Open(profile, selection), cancellationToken).ConfigureAwait(false);

        // AssetRegistry.bin is parsed lazily on the first type lookup (~5s for a 340k-asset game).
        // Warm it now so the first tree/search request after mounting stays fast.
        var warmProvider = session.Provider;
        _ = Task.Run(() =>
        {
            try { AssetRegistryCache.GetStatus(warmProvider); }
            catch { /* warm-up only */ }
        }, CancellationToken.None);

        lock (_configLock)
        {
            Config.UiState.LastProfile = profile.Name;
            if (Config.Profiles.ContainsKey(profile.Name)) SaveConfigLocked();
        }

        return session.ToSummary();
    }

    public void Close(string sessionId)
    {
        Sessions.Close(sessionId);
        foreach (var key in _animationCache.Keys)
        {
            if (key.StartsWith(sessionId + "|", StringComparison.OrdinalIgnoreCase)) _animationCache.TryRemove(key, out _);
        }
        lock (_configLock) SaveConfigLocked();
    }

    public Task<PageResult<TreeEntry>> BrowseAsync(string sessionId, string? path, int limit, int offset, CancellationToken ct = default)
    {
        var session = Sessions.Get(sessionId);
        // Path enumeration only touches the already-mounted file dictionary; taking the session
        // gate here serialised browsing behind a running mesh export (the sidebar froze).
        session.Touch();
        return Task.Run(() => AssetQuery.Browse(session.Provider, path, limit, offset, session.Profile), ct);
    }

    public Task<PageResult<TreeEntry>> FindAsync(string sessionId, string query, string? prefix, string? extension, int limit, int offset, bool includeNonAssets, string? kind = null, bool nameOnly = false, CancellationToken ct = default)
    {
        var session = Sessions.Get(sessionId);
        session.Touch();
        return Task.Run(() => AssetQuery.Find(session.Provider, query, prefix, extension, limit, offset, includeNonAssets, kind, nameOnly, session.Profile), ct);
    }

    public object GetAssetTypes(string profile, string? sessionId)
    {
        GameProfile game;
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profile, out game!))
                throw AssetException.NotFound($"profile not found: {profile}");
        }

        object? registry = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            try { registry = AssetRegistryCache.GetStatus(Sessions.Get(sessionId).Provider); }
            catch { /* session gone */ }
        }

        return new
        {
            profile,
            rules = game.PrefixRules,
            builtIn = AssetKind.BuiltInRules,
            registry
        };
    }

    public object SetPrefixRules(string profile, IReadOnlyList<PrefixRule> rules)
    {
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profile, out var game))
                throw AssetException.NotFound($"profile not found: {profile}");
            game.PrefixRules = rules
                .Where(r => !string.IsNullOrWhiteSpace(r.Prefix))
                .Select(r => new PrefixRule { Prefix = r.Prefix.Trim(), Kind = string.IsNullOrWhiteSpace(r.Kind) ? "asset" : r.Kind!.Trim() })
                .ToList();
            SaveConfigLocked();
            return new { profile, rules = game.PrefixRules };
        }
    }

    public IReadOnlyList<TreeEntry> ListAnimations(string sessionId, string meshPath, string? prefix, string? query, int limit)
    {
        var session = Sessions.Get(sessionId);
        var provider = session.Provider;
        var results = new Dictionary<string, TreeEntry>(StringComparer.OrdinalIgnoreCase);
        var max = Math.Clamp(limit, 1, 200);
        var cacheKey = sessionId + "|" + meshPath + "|" + (prefix ?? "") + "|" + (query ?? "") + "|" + max;
        if (_animationCache.TryGetValue(cacheKey, out var cached)) return cached;

        var directories = new List<string>();
        if (!string.IsNullOrWhiteSpace(prefix)) directories.Add(prefix!.TrimEnd('/'));
        else
        {
            var directory = (Path.GetDirectoryName(meshPath) ?? "").Replace('\\', '/').TrimEnd('/');
            if (directory.Length > 0)
            {
                directories.Add(directory);
                directories.Add(directory + "/Anim");
                directories.Add(directory + "/Animations");
                var parent = Path.GetDirectoryName(directory)?.Replace('\\', '/').TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    directories.Add(parent + "/Anim");
                    directories.Add(parent + "/Animations");
                }
            }

            var segments = meshPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 4) directories.Add(string.Join('/', segments.Take(3)) + "/Animation");
        }

        foreach (var entry in AssetQuery.FindInDirectories(provider, "AS_", directories, ".uasset", max, "anim_sequence", session.Profile))
            results[entry.Path] = entry;

        if (results.Count == 0)
        {
            var searchTerms = new List<string>();
            if (!string.IsNullOrWhiteSpace(query)) searchTerms.Add(query!);
            else
            {
                var stem = Path.GetFileNameWithoutExtension(meshPath);
                searchTerms.AddRange(stem.Split('_', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 3 && !t.Equals("body", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.Length));
            }

            foreach (var term in searchTerms.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var page = AssetQuery.Find(provider, term, null, ".uasset", max, 0, false, "anim_sequence", false, session.Profile);
                    foreach (var entry in page.Items) results[entry.Path] = entry;
                }
                catch
                {
                    // ignore a term that cannot be searched
                }
                if (results.Count >= max) break;
            }
        }

        var list = results.Values
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
        if (list.Count > 0) _animationCache[cacheKey] = list;
        return list;
    }

    public SearchResult SearchAssets(string sessionId, string query, string? kind, int limit)
    {
        var session = Sessions.Get(sessionId);
        var provider = session.Provider;
        var aliases = AssetAliases.Expand(query);
        var max = Math.Clamp(limit, 1, 200);
        var found = new Dictionary<string, TreeEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in aliases)
        {
            if (found.Count >= max * 4) break;
            try
            {
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    var typed = AssetQuery.Find(provider, term, null, ".uasset", Math.Max(max * 4, 200), 0, false, kind, false, session.Profile);
                    foreach (var entry in typed.Items) found[entry.Path] = entry;
                }
                else
                {
                    // Models first: a broad term like "Xiong" matches hundreds of textures and
                    // blueprints, and the mesh can fall outside a plain path-ordered page.
                    foreach (var meshKind in new[] { "skeletal_mesh", "static_mesh" })
                    {
                        var meshes = AssetQuery.Find(provider, term, null, ".uasset", Math.Max(max * 2, 100), 0, false, meshKind, false, session.Profile);
                        foreach (var entry in meshes.Items) found[entry.Path] = entry;
                    }
                }
            }
            catch
            {
                // ignore a term that cannot be searched
            }
        }

        if (found.Count == 0)
        {
            foreach (var term in aliases)
            {
                try
                {
                    var page = AssetQuery.Find(provider, term, null, ".uasset", Math.Max(max * 4, 200), 0, false, kind, false, session.Profile);
                    foreach (var entry in page.Items) found[entry.Path] = entry;
                }
                catch { }
            }
        }

        static int Rank(TreeEntry entry) => entry.Kind switch
        {
            "skeletal_mesh" => 0,
            "static_mesh" => 1,
            "blueprint" => 2,
            "anim_blueprint" => 3,
            "anim_sequence" or "anim_montage" => 4,
            "material_instance" or "material" => 5,
            _ => 9
        };

        var items = found.Values
            .OrderBy(Rank)
            .ThenBy(entry => entry.Path.Length)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
        return new SearchResult(query, aliases, items, found.Count);
    }

    public Task<JObject> AnalyzeAsync(string sessionId, string objectPath, bool includeRaw, CancellationToken ct = default)
    {
        var session = Sessions.Get(sessionId);
        return WithGateAsync(session, () => AssetAnalyzer.Analyze(session.Provider, objectPath, includeRaw, Config.Limits.MaxResponseChars), ct);
    }

    public IndexStatus BuildIndex(string sessionId)
    {
        var session = Sessions.Get(sessionId);
        return AssetIndex.Build(session.Provider, IndexRoot, session.Profile, session.Paks);
    }

    public IndexStatus IndexStatus(string profile)
    {
        GameProfile game;
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profile, out game!))
                throw AssetException.NotFound($"profile not found: {profile}");
        }
        return AssetIndex.Status(AssetIndex.DbPath(IndexRoot, profile), game);
    }

    public IndexPage SearchIndex(string profile, string query, string? kind, bool nameOnly, int limit, int offset)
    {
        GameProfile game;
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profile, out game!))
                throw AssetException.NotFound($"profile not found: {profile}");
        }
        return AssetIndex.Search(AssetIndex.DbPath(IndexRoot, profile), game, query, kind, nameOnly, limit, offset);
    }

    public bool DropIndex(string profile)
    {
        lock (_configLock)
        {
            if (!Config.Profiles.ContainsKey(profile))
                throw AssetException.NotFound($"profile not found: {profile}");
        }
        return AssetIndex.Drop(AssetIndex.DbPath(IndexRoot, profile));
    }

    public Task<InspectResult> InspectAsync(string sessionId, string objectPath, string section, int exportLimit, CancellationToken ct = default)
    {
        var session = Sessions.Get(sessionId);
        var maxChars = Config.Limits.MaxResponseChars;
        return WithGateAsync(session, () => ObjectReader.Inspect(session.Provider, objectPath, section, maxChars, exportLimit), ct);
    }

    public async Task<PreviewResult> PreviewAsync(string sessionId, string objectPath, int maxSize, string? animationPath = null, CancellationToken ct = default)
    {
        var session = Sessions.Get(sessionId);
        await session.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            session.Touch();
            var obj = ObjectReader.Load(session.Provider, objectPath);
            if (obj is CUE4Parse.UE4.Assets.Exports.Texture.UTexture texture)
                return TexturePreview.Render(texture, objectPath, maxSize);

            if (MeshPreview.IsMesh(obj))
            {
                // Mesh export pulls in referenced materials/textures and can take seconds;
                // run it off the caller's thread while the session gate stays held.
                var mesh = await Task.Run(
                    () => MeshPreview.ExportAsync(session.Provider, objectPath, CacheRoot, ct),
                    ct).ConfigureAwait(false);

                object? animation = null;
                string? animationError = null;
                if (!string.IsNullOrWhiteSpace(animationPath))
                {
                    try
                    {
                        var clip = AnimationPreview.Export(session.Provider, animationPath!, CacheRoot);
                        animation = new { url = "/ue-asset/cache/" + clip.RelativeEntry, clip = clip.Info };
                    }
                    catch (AssetException ex)
                    {
                        // Keep the mesh preview usable; report the codec gap separately.
                        animationError = ex.Message;
                    }
                }

                var meta = new
                {
                    modelUrl = "/ue-asset/cache/" + mesh.RelativeEntry,
                    animation,
                    animationError,
                    bytes = mesh.Bytes,
                    fileCount = mesh.Files.Count,
                    animations = mesh.Animations,
                    materials = mesh.Materials,
                    materialBindings = mesh.MaterialBindings
                };
                return new PreviewResult(objectPath, obj.ExportType, "mesh", null, null, null, null, meta, "拖动旋转 · 滚轮缩放");
            }

            var inspected = ObjectReader.Inspect(session.Provider, objectPath, "all", 4000, 50);
            var properties = new
            {
                inspected.Meta.Size,
                inspected.Meta.ExportCount,
                inspected.Meta.ImportCount,
                exports = inspected.Exports.Take(20)
            };
            return new PreviewResult(objectPath, inspected.Meta.ClassName ?? obj.GetType().Name, "properties", null, null, null, null, properties, "no raster preview for this asset type; returning properties");
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task<MeshPreviewResult> MeshAsync(string sessionId, string objectPath, CancellationToken ct = default)
    {
        var session = Sessions.Get(sessionId);
        await session.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            session.Touch();
            return await Task.Run(
                () => MeshPreview.ExportAsync(session.Provider, objectPath, CacheRoot, ct),
                ct).ConfigureAwait(false);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public string Export(string sessionId, IReadOnlyList<string> paths)
    {
        var session = Sessions.Get(sessionId);
        var format = Config.DefaultMeshFormat;
        var maxBatch = Config.Limits.MaxBatchAssets;
        var outputRoot = Config.OutputRoot ?? AppConfig.DefaultOutputRoot();
        Directory.CreateDirectory(outputRoot);

        return session.Jobs.Enqueue("export", paths.Count, async (job, ct) =>
        {
            await session.Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var (results, outputDirectory) = await ExportService
                    .RunAsync(session.Provider, paths, outputRoot, format, maxBatch, job, ct)
                    .ConfigureAwait(false);

                lock (job.Results)
                {
                    job.Results.Clear();
                    job.Results.AddRange(results);
                }

                job.Message = $"exported {results.Count(r => r.Success)}/{results.Count} requested asset(s)";
            }
            finally
            {
                session.Gate.Release();
            }
        });
    }

    public JobView? Job(string action, string? sessionId, string? jobId)
    {
        switch ((action ?? "list").ToLowerInvariant())
        {
            case "list":
            {
                var queues = string.IsNullOrWhiteSpace(sessionId)
                    ? Sessions.All
                    : [Sessions.Get(sessionId!)];
                return null; // list is returned through Jobs(), see below
            }
            case "status":
            case "results":
            {
                if (string.IsNullOrWhiteSpace(jobId)) throw AssetException.Invalid("jobId is required");
                foreach (var session in Sessions.All)
                {
                    var view = session.Jobs.Get(jobId!);
                    if (view is not null) return view;
                }
                throw AssetException.NotFound($"job not found: {jobId}");
            }
            case "cancel":
            {
                if (string.IsNullOrWhiteSpace(jobId)) throw AssetException.Invalid("jobId is required");
                foreach (var session in Sessions.All)
                {
                    if (session.Jobs.Get(jobId!) is not null)
                    {
                        if (!session.Jobs.Cancel(jobId!)) throw AssetException.Invalid($"job cannot be cancelled: {jobId}");
                        return session.Jobs.Get(jobId!);
                    }
                }
                throw AssetException.NotFound($"job not found: {jobId}");
            }
            default:
                throw AssetException.Invalid($"unknown job action: {action}");
        }
    }

    public IReadOnlyList<JobView> Jobs(string? sessionId)
    {
        var queues = string.IsNullOrWhiteSpace(sessionId)
            ? Sessions.All
            : [Sessions.Get(sessionId!)];
        return queues.SelectMany(s => s.Jobs.List()).OrderByDescending(j => j.CreatedAt).ToList();
    }

    public ConfigView GetConfig()
    {
        lock (_configLock)
        {
            return new ConfigView(
                Store.Path,
                CacheRoot,
                Config.Profiles.Values.Select(p => new ProfileView(
                    p.Name,
                    p.Directory,
                    [.. p.ExtraDirectories],
                    p.Game,
                    p.Platform,
                    !string.IsNullOrWhiteSpace(p.AesKey),
                    p.DynamicKeys.Count,
                    p.Usmap,
                    p.PrefixRules,
                    p.DynamicKeys.Select(k => new DynamicKeyInput { Guid = k.Guid, Key = k.Key }).ToList())).ToList(),
                Config.OutputRoot ?? AppConfig.DefaultOutputRoot(),
                Config.OodleLibrary,
                Config.DefaultMeshFormat,
                Config.Limits.MaxSessions,
                Config.Limits.MaxJobs,
                Config.Limits.MaxBatchAssets,
                Config.UiState.LastProfile);
        }
    }

    /// <summary>Selectable values of the parser's EGame enum, reflected so the UI never hard-codes them.</summary>
    public IReadOnlyList<GameOption> ListGames() => GameCatalog.All();

    /// <summary>
    /// Icon of the game executable (PNG, base64) for the settings cards. A missing executable or icon
    /// returns null so the UI keeps its own placeholder instead of failing the request.
    /// </summary>
    public object ProfileIcon(string profileName)
    {
        GameProfile profile;
        lock (_configLock)
        {
            if (!Config.Profiles.TryGetValue(profileName, out var found))
                throw AssetException.NotFound($"profile not found: {profileName}");
            profile = found.Clone();
        }

        var (exe, png) = GameIcon.Resolve(profile);
        return new
        {
            icon = png is null ? null : Convert.ToBase64String(png),
            exe
        };
    }

    /// <summary>
    /// Resolves the Oodle runtime for the settings button. A local copy (game folder, FModel, next to
    /// the executable) always wins; the community mirror is downloaded only when nothing is found, and
    /// the resolved path is persisted so the next launch reuses it.
    /// </summary>
    public async Task<object> AcquireOodleAsync(string? profileName, CancellationToken cancellationToken = default)
    {
        GameProfile? profile;
        string? configured;
        lock (_configLock)
        {
            configured = Config.OodleLibrary;
            profile = !string.IsNullOrWhiteSpace(profileName) && Config.Profiles.TryGetValue(profileName!, out var named)
                ? named.Clone()
                : Config.Profiles.Values.Select(item => item.Clone()).FirstOrDefault();
        }

        var (path, method) = await DshUeAsset.Core.Sessions.OodleAcquirer
            .AcquireAsync(profile, configured, AppContext.BaseDirectory, cancellationToken)
            .ConfigureAwait(false);

        lock (_configLock)
        {
            Config.OodleLibrary = path;
            SaveConfigLocked();
        }

        return new { path, method };
    }

    public ConfigView SaveProfile(ProfileInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw AssetException.Invalid("profile name is required");
        if (string.IsNullOrWhiteSpace(input.Directory)) throw AssetException.Invalid("profile directory is required");

        var directory = NormalizeDirectory(input.Directory);
        if (!Directory.Exists(directory)) throw AssetException.NotFound($"directory does not exist: {directory}");

        lock (_configLock)
        {
            var key = input.Name.Trim();
            if (!string.IsNullOrWhiteSpace(input.OriginalName) &&
                !key.Equals(input.OriginalName, StringComparison.OrdinalIgnoreCase) &&
                Config.Profiles.ContainsKey(key))
                throw AssetException.Invalid($"profile already exists: {key}");

            Config.Profiles.TryGetValue(input.OriginalName ?? key, out var existing);
            var profile = existing?.Clone() ?? new GameProfile();
            if (existing is not null && !string.IsNullOrWhiteSpace(input.OriginalName) &&
                !configKeyEquals(input.OriginalName!, key))
            {
                Config.Profiles.Remove(input.OriginalName!);
            }

            profile.Name = key;
            profile.Directory = directory;
            profile.Game = string.IsNullOrWhiteSpace(input.Game) ? "GAME_UE5_0" : input.Game!.Trim();
            profile.Platform = string.IsNullOrWhiteSpace(input.Platform) ? "DesktopMobile" : input.Platform!.Trim();
            profile.ExtraDirectories = input.ExtraDirectories?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(Path.GetFullPath).ToList() ?? profile.ExtraDirectories;
            profile.Usmap = string.IsNullOrWhiteSpace(input.Usmap) ? null : Path.GetFullPath(input.Usmap);

            if (input.ClearAesKey) profile.AesKey = null;
            else if (!string.IsNullOrWhiteSpace(input.AesKey)) profile.AesKey = input.AesKey.Trim();

            if (input.PrefixRules is not null)
            {
                profile.PrefixRules = input.PrefixRules
                    .Where(r => !string.IsNullOrWhiteSpace(r.Prefix))
                    .Select(r => new PrefixRule { Prefix = r.Prefix.Trim(), Kind = string.IsNullOrWhiteSpace(r.Kind) ? "asset" : r.Kind.Trim() })
                    .ToList();
            }

            if (input.DynamicKeys is not null)
            {
                profile.DynamicKeys = input.DynamicKeys
                    .Where(k => !string.IsNullOrWhiteSpace(k.Guid) && !string.IsNullOrWhiteSpace(k.Key))
                    .Select(k => new DynamicKey { Guid = k.Guid.Trim(), Key = k.Key.Trim() })
                    .ToList();
            }

            Config.Profiles[key] = profile;
            SaveConfigLocked();
            return GetConfig();
        }
    }

    public ConfigView DeleteProfile(string name)
    {
        lock (_configLock)
        {
            if (!Config.Profiles.Remove(name)) throw AssetException.NotFound($"profile not found: {name}");
            if (Config.UiState.LastProfile == name) Config.UiState.LastProfile = null;
            SaveConfigLocked();
            return GetConfig();
        }
    }

    public ConfigView SaveGlobal(GlobalInput input)
    {
        lock (_configLock)
        {
            if (!string.IsNullOrWhiteSpace(input.OutputRoot))
            {
                var root = Path.GetFullPath(input.OutputRoot);
                Directory.CreateDirectory(root);
                Config.OutputRoot = root;
            }
            if (input.CacheRoot is not null)
            {
                if (string.IsNullOrWhiteSpace(input.CacheRoot))
                {
                    Config.CacheRoot = null;
                }
                else
                {
                    var root = Path.GetFullPath(input.CacheRoot);
                    Directory.CreateDirectory(root);
                    Config.CacheRoot = root;
                }
            }
            if (input.OodleLibrary is not null)
                Config.OodleLibrary = string.IsNullOrWhiteSpace(input.OodleLibrary) ? null : Path.GetFullPath(input.OodleLibrary);
            if (!string.IsNullOrWhiteSpace(input.DefaultMeshFormat)) Config.DefaultMeshFormat = input.DefaultMeshFormat!;
            if (input.MaxSessions is > 0) Config.Limits.MaxSessions = input.MaxSessions.Value;
            if (input.MaxJobs is > 0) Config.Limits.MaxJobs = input.MaxJobs.Value;
            if (input.MaxBatchAssets is > 0) Config.Limits.MaxBatchAssets = input.MaxBatchAssets.Value;

            SaveConfigLocked();
            return GetConfig();
        }
    }

    public ImportResult ImportFmodel(string? path)
    {
        lock (_configLock)
        {
            var result = FmodelImporter.Import(Config, path);
            SaveConfigLocked();
            return result;
        }
    }

    public void SaveConfig()
    {
        lock (_configLock) SaveConfigLocked();
    }

    private void SaveConfigLocked() => _store.Save(Config);

    private GameProfile ResolveProfile(OpenRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Profile))
        {
            if (!Config.Profiles.TryGetValue(request.Profile!, out var profile))
                throw AssetException.NotFound($"profile not found: {request.Profile}", new { profiles = Config.Profiles.Keys.ToArray() });
            return profile.Clone();
        }

        if (string.IsNullOrWhiteSpace(request.Directory))
            throw AssetException.Invalid("pass a profile name or a paks directory");

        var directory = NormalizeDirectory(request.Directory!);
        var name = new DirectoryInfo(directory).Name;
        // An ad-hoc directory such as ".../Paks/~mods" carries no engine version of its own. Opening it
        // as GAME_UE5_0 makes CUE4Parse silently drop every reader (mounted == 0), so a directory that
        // lives inside a configured profile inherits that profile's container settings.
        var inherited = InheritFromConfigured(directory);
        return new GameProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "ad-hoc" : name,
            Directory = directory,
            Game = !string.IsNullOrWhiteSpace(request.Game) ? request.Game! : inherited?.Game ?? "GAME_UE5_0",
            Platform = inherited?.Platform ?? "DesktopMobile",
            AesKey = !string.IsNullOrWhiteSpace(request.AesKey) ? request.AesKey : inherited?.AesKey,
            Usmap = !string.IsNullOrWhiteSpace(request.Usmap) ? request.Usmap : inherited?.Usmap,
            DynamicKeys = inherited is null
                ? []
                : inherited.DynamicKeys.Select(k => new DynamicKey { Guid = k.Guid, Key = k.Key }).ToList()
        };
    }

    /// <summary>
    /// Longest configured profile whose paks directory contains <paramref name="directory"/>.
    /// Used to inherit game/AES/usmap for ad-hoc subdirectories such as "~mods".
    /// </summary>
    private GameProfile? InheritFromConfigured(string directory)
    {
        GameProfile? best = null;
        var bestLength = -1;
        foreach (var candidate in Config.Profiles.Values)
        {
            var root = GameProfile.NormalizeDirectory(candidate.Directory);
            if (root.Length == 0) continue;
            var matches = string.Equals(root, directory, StringComparison.OrdinalIgnoreCase)
                          || directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!matches || root.Length <= bestLength) continue;
            best = candidate;
            bestLength = root.Length;
        }
        return best?.Clone();
    }

    private static string NormalizeDirectory(string input)
    {
        var directory = Path.GetFullPath(input);
        var paks = Path.Combine(directory, "Content", "Paks");
        if (!Directory.Exists(directory)) return directory;
        var hasArchives = Directory.EnumerateFiles(directory, "*.pak", SearchOption.TopDirectoryOnly).Any()
                          || Directory.EnumerateFiles(directory, "*.utoc", SearchOption.TopDirectoryOnly).Any();
        if (!hasArchives && Directory.Exists(paks)) return paks;
        return directory;
    }

    private static bool configKeyEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static async Task<T> WithGateAsync<T>(GameSession session, Func<T> work, CancellationToken ct)
    {
        await session.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            session.Touch();
            return await Task.Run(work, ct).ConfigureAwait(false);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public void Dispose()
    {
        Sessions.CloseAll();
    }
}