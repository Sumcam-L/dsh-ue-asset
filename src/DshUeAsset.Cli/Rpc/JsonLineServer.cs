using DshUeAsset.Core.Config;
using System.Collections.Concurrent;
using DshUeAsset.Core;
using DshUeAsset.Core.Models;
using DshUeAsset.Core.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DshUeAsset.Cli.Rpc;

/// <summary>
/// Newline-delimited JSON RPC over stdin/stdout. stdout is the protocol channel and must contain
/// nothing else; Serilog and all diagnostics go to stderr.
/// </summary>
public sealed class JsonLineServer
{
    private readonly AssetService _service;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    public JsonLineServer(AssetService service)
    {
        _service = service;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Console.Error.WriteLineAsync("[ueasset] serve ready").ConfigureAwait(false);
        var pending = new ConcurrentBag<Task>();

        while (!cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var request = line;
            var task = Task.Run(() => HandleAsync(request), CancellationToken.None);
            pending.Add(task);
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    private async Task HandleAsync(string line)
    {
        JObject request;
        try
        {
            request = JObject.Parse(line);
        }
        catch (Exception ex)
        {
            await WriteAsync(new { id = (string?)null, ok = false, error = new { code = "invalid_argument", message = "invalid JSON: " + ex.Message } }).ConfigureAwait(false);
            return;
        }

        var id = request["id"]?.ToString();
        var method = request["method"]?.ToString() ?? "";
        var parameters = request["params"] as JObject ?? new JObject();

        try
        {
            var result = await DispatchAsync(method, parameters).ConfigureAwait(false);
            await WriteAsync(new { id, ok = true, result }).ConfigureAwait(false);
        }
        catch (AssetException ex)
        {
            await WriteAsync(new { id, ok = false, error = new { code = ErrorCodes.Of(ex.Kind), message = ex.Message, details = ex.Details } }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("[ueasset] " + ex).ConfigureAwait(false);
            await WriteAsync(new { id, ok = false, error = new { code = "internal", message = ex.Message } }).ConfigureAwait(false);
        }
    }

    private async Task<object?> DispatchAsync(string method, JObject p)
    {
        switch (method)
        {
            case "ping":
                return new { version = "0.1.0", pid = Environment.ProcessId, config = _service.Store.Path };

            case "config.get":
                return _service.GetConfig();

            case "config.games":
                return new { games = _service.ListGames() };

            case "profile.icon":
                return _service.ProfileIcon(Required(p, "profile"));

            case "oodle.acquire":
                return await _service.AcquireOodleAsync(Optional(p, "profile")).ConfigureAwait(false);

            case "config.saveGlobal":
                return _service.SaveGlobal(p.ToObject<GlobalInput>() ?? new GlobalInput());

            case "config.saveProfile":
                return _service.SaveProfile(p.ToObject<ProfileInput>() ?? throw AssetException.Invalid("profile body is required"));

            case "config.deleteProfile":
                return _service.DeleteProfile(Required(p, "name"));

            case "config.importFmodel":
                return _service.ImportFmodel(Optional(p, "path"));

            case "open":
            {
                var request = new OpenRequest(
                    Optional(p, "profile"),
                    Optional(p, "directory"),
                    Optional(p, "game"),
                    Optional(p, "aesKey"),
                    Optional(p, "usmap"),
                    p["paks"]?.ToObject<string[]>(),
                    p["tags"]?.ToObject<string[]>());
                return await _service.OpenAsync(request).ConfigureAwait(false);
            }

            case "searchAssets":
                return _service.SearchAssets(
                    Required(p, "sessionId"),
                    Required(p, "query"),
                    Optional(p, "kind"),
                    Int(p, "limit", 50));

            case "assetTypes":
            {
                var action = Optional(p, "action") ?? "get";
                if (action == "set")
                    return _service.SetPrefixRules(Required(p, "profile"), p["rules"]?.ToObject<List<PrefixRule>>() ?? []);
                return _service.GetAssetTypes(Required(p, "profile"), Optional(p, "sessionId"));
            }

            case "animations":
                return _service.ListAnimations(
                    Required(p, "sessionId"),
                    Required(p, "mesh"),
                    Optional(p, "prefix"),
                    Optional(p, "query"),
                    Int(p, "limit", 100));

            case "asset":
                return await _service.AnalyzeAsync(
                    Required(p, "sessionId"),
                    Required(p, "path"),
                    Bool(p, "raw")).ConfigureAwait(false);

            case "index":
            {
                var action = Optional(p, "action") ?? "status";
                return action switch
                {
                    "build" => _service.BuildIndex(Required(p, "sessionId")),
                    "status" => _service.IndexStatus(Required(p, "profile")),
                    "search" => _service.SearchIndex(
                        Required(p, "profile"),
                        Optional(p, "query") ?? "",
                        Optional(p, "kind"),
                        Bool(p, "nameOnly"),
                        Int(p, "limit", 50),
                        Int(p, "offset", 0)),
                    "drop" => _service.DropIndex(Required(p, "profile")),
                    _ => throw AssetException.Invalid($"unknown index action: {action}")
                };
            }

            case "paks":
                return _service.ListPaks(Required(p, "profile"));

            case "getTag":
                return _service.GetPakTags(Required(p, "profile"));

            case "setTag":
                return _service.SetPakTags(
                    Required(p, "profile"),
                    Required(p, "pak"),
                    p["tags"]?.ToObject<string[]>() ?? []);

            case "pakKeys":
                return _service.ListPakKeys(Required(p, "profile"));

            case "bindPakKey":
                return _service.BindPakKey(
                    Required(p, "profile"),
                    Required(p, "pak"),
                    Required(p, "key"));

            case "close":
                _service.Close(Required(p, "sessionId"));
                return new { closed = true };

            case "sessions":
                return _service.ListSessions();

            case "tree":
                return await _service.BrowseAsync(Required(p, "sessionId"), Optional(p, "path"), Int(p, "limit", 200), Int(p, "offset", 0)).ConfigureAwait(false);

            case "find":
                return await _service.FindAsync(
                    Required(p, "sessionId"),
                    Optional(p, "query") ?? "",
                    Optional(p, "prefix"),
                    Optional(p, "ext"),
                    Int(p, "limit", 100),
                    Int(p, "offset", 0),
                    Bool(p, "includeNonAssets"),
                    Optional(p, "kind"),
                    Bool(p, "nameOnly")).ConfigureAwait(false);

            case "inspect":
                return await _service.InspectAsync(
                    Required(p, "sessionId"),
                    Required(p, "path"),
                    Optional(p, "section") ?? "all",
                    Int(p, "exportLimit", 100)).ConfigureAwait(false);

            case "mesh":
                return await _service.MeshAsync(
                    Required(p, "sessionId"),
                    Required(p, "path")).ConfigureAwait(false);

            case "preview":
                return await _service.PreviewAsync(
                    Required(p, "sessionId"),
                    Required(p, "path"),
                    Int(p, "maxSize", 1024),
                    Optional(p, "animationPath")).ConfigureAwait(false);

            case "export":
            {
                var paths = p["paths"]?.ToObject<string[]>() ?? [];
                if (paths.Length == 0) throw AssetException.Invalid("paths is required");
                return new { jobId = _service.Export(Required(p, "sessionId"), paths) };
            }

            case "job":
                return _service.Job(Optional(p, "action") ?? "status", Optional(p, "sessionId"), Optional(p, "jobId"));

            case "jobs":
                return _service.Jobs(Optional(p, "sessionId"));

            case "shutdown":
                _shutdown.Cancel();
                return new { shuttingDown = true };

            default:
                throw AssetException.Invalid($"unknown method: {method}");
        }
    }

    private async Task WriteAsync(object payload)
    {
        var line = CliJson.Compact(payload);
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string Required(JObject p, string name)
    {
        var value = Optional(p, name);
        return string.IsNullOrWhiteSpace(value) ? throw AssetException.Invalid($"missing parameter: {name}") : value!;
    }

    private static string? Optional(JObject p, string name)
    {
        var token = p[name];
        return token is null || token.Type == JTokenType.Null ? null : token.ToString();
    }

    private static int Int(JObject p, string name, int fallback)
    {
        var token = p[name];
        return token is null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
    }

    private static bool Bool(JObject p, string name)
    {
        var token = p[name];
        return token is not null && token.Type != JTokenType.Null && token.Value<bool>();
    }
}