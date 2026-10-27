using System.Text;
using DshUeAsset.Cli;
using DshUeAsset.Cli.Rpc;
using DshUeAsset.Core;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Models;
using DshUeAsset.Core.Services;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    if (arg.StartsWith("--", StringComparison.Ordinal))
    {
        var key = arg[2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            options[key] = args[++i];
        }
        else
        {
            options[key] = null;
        }
    }
    else
    {
        positional.Add(arg);
    }
}

string? Opt(string name) => options.TryGetValue(name, out var value) ? value : null;
bool Flag(string name) => options.ContainsKey(name) && Opt(name) is not "false";

try
{
    var store = new ConfigStore(Opt("config"));
    using var service = new AssetService(store);
    var command = positional.Count > 0 ? positional[0] : "help";

    switch (command)
    {
        case "help" or "-h" or "--help":
            PrintHelp();
            return 0;

        case "serve":
        {
            var server = new JsonLineServer(service);
            await server.RunAsync(CancellationToken.None);
            return 0;
        }

        case "profiles":
        {
            Print(service.GetConfig());
            return 0;
        }

        case "config":
        {
            var sub = positional.Count > 1 ? positional[1] : "get";
            switch (sub)
            {
                case "path":
                    Console.WriteLine(store.Path);
                    return 0;
                case "get":
                    Print(service.GetConfig());
                    return 0;
                case "import-fmodel":
                {
                    var result = service.ImportFmodel(Opt("source"));
                    Print(result);
                    return result.Imported.Count > 0 ? 0 : 1;
                }
                default:
                    throw AssetException.Invalid($"unknown config subcommand: {sub}");
            }
        }

        case "paks":
        {
            var name = positional.Count > 1 ? positional[1] : Opt("profile");
            if (string.IsNullOrWhiteSpace(name)) throw AssetException.Invalid("paks requires a profile name");
            var catalog = service.ListPaks(name!);
            if (Flag("json")) Print(catalog);
            else
            {
                foreach (var pak in catalog.Available)
                    Console.WriteLine($"{pak.Name}  {pak.Size / 1024 / 1024}MB  chunk={pak.Chunk}  tags=[{string.Join(",", pak.Tags)}]");
                Console.WriteLine($"total={catalog.Available.Count} defaults=[{string.Join(",", catalog.Defaults)}]");
            }
            return 0;
        }

        case "tag":
        {
            var sub = positional.Count > 1 ? positional[1] : "get";
            var profile = Opt("profile") ?? (positional.Count > 2 ? positional[2] : null);
            if (string.IsNullOrWhiteSpace(profile)) throw AssetException.Invalid("tag requires --profile");
            if (sub == "get") { Print(service.GetPakTags(profile)); return 0; }
            var pak = Opt("pak") ?? (positional.Count > 3 ? positional[3] : null);
            if (string.IsNullOrWhiteSpace(pak)) throw AssetException.Invalid("tag set requires --pak");
            Print(service.SetPakTags(profile, pak!, SplitOption("tags") ?? []));
            return 0;
        }

        case "doctor":
        {
            var name = positional.Count > 1 ? positional[1] : Opt("profile");
            if (string.IsNullOrWhiteSpace(name)) throw AssetException.Invalid("doctor requires a profile name");
            if (!service.Config.Profiles.TryGetValue(name!, out var profile)) throw AssetException.NotFound($"profile not found: {name}");
            await Console.Error.WriteLineAsync($"directory={profile.Directory} exists={Directory.Exists(profile.Directory)}");
            var pakFiles = Directory.Exists(profile.Directory)
                ? Directory.GetFiles(profile.Directory, "*.pak", SearchOption.TopDirectoryOnly).Length
                : 0;
            await Console.Error.WriteLineAsync($"pakFiles={pakFiles}");
            if (!string.IsNullOrWhiteSpace(service.Config.OodleLibrary))
            {
                try { OodleHelper.Initialize(service.Config.OodleLibrary); await Console.Error.WriteLineAsync("oodle=ok"); }
                catch (Exception ex) { await Console.Error.WriteLineAsync("oodle=fail " + ex.Message); }
            }
            var versions = new VersionContainer(
                game: Enum.Parse<EGame>(profile.Game, true),
                platform: Enum.Parse<ETexturePlatform>(profile.Platform, true));
            var provider = new DefaultFileProvider(new DirectoryInfo(profile.Directory), [], SearchOption.TopDirectoryOnly, versions, StringComparer.OrdinalIgnoreCase);
            provider.Initialize();
            await Console.Error.WriteLineAsync($"unloadedVfs={provider.UnloadedVfs.Count} files={provider.Files.Count} requiredKeys={provider.RequiredKeys.Count}");
            if (!string.IsNullOrWhiteSpace(profile.AesKey))
            {
                var key = profile.AesKey.Trim();
                if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) key = key[2..];
                await Console.Error.WriteLineAsync($"submitKey={provider.SubmitKey(new FGuid(), new FAesKey(key))}");
            }
            await Console.Error.WriteLineAsync($"remainingKeys={provider.RequiredKeys.Count}");
            var mounted = provider.Mount();
            provider.PostMount();
            await Console.Error.WriteLineAsync($"mounted={mounted} files={provider.Files.Count}");
            foreach (var vfs in provider.MountedVfs.Take(5)) await Console.Error.WriteLineAsync("vfs=" + vfs.Name);

            if (positional.Count > 2)
            {
                var asset = positional[2];
                var saved = provider.SaveAsset(asset);
                await Console.Error.WriteLineAsync($"saveAsset={saved.Length}");
                var fileKey = provider.Files.Keys.FirstOrDefault(k => k.Equals(asset, StringComparison.OrdinalIgnoreCase));
                await Console.Error.WriteLineAsync($"fileKey=[{fileKey}]");
                if (fileKey is not null && provider.Files.TryGetValue(fileKey, out var gameFile))
                {
                    provider.Files.FindPayloads(gameFile, out var uexp, out var ubulks, out var uptnls, false);
                    await Console.Error.WriteLineAsync($"payload uexp=[{uexp?.Path}] bulks={ubulks.Count} uptnls={uptnls.Count}");
                }
                foreach (var k in provider.Files.Keys.Where(k => k.Contains("T_bossrushdifficulty_levelbarbglight", StringComparison.OrdinalIgnoreCase)))
                    await Console.Error.WriteLineAsync($"key=[{k}]");
                var package = provider.LoadPackage(asset);
                await Console.Error.WriteLineAsync($"package={package.Name} exports={package.ExportMapLength} imports={package.ImportMapLength} fullyLoaded={package.IsFullyLoaded}");
                var export = package.GetExports().FirstOrDefault();
                if (export is not null)
                {
                    await Console.Error.WriteLineAsync($"export={export.Name} type={export.ExportType} properties={export.Properties.Count}");
                    if (export is UTexture texture)
                    {
                        await Console.Error.WriteLineAsync($"texture format={texture.Format} platform={texture.PlatformData.GetType().Name} size={texture.PlatformData.SizeX}x{texture.PlatformData.SizeY} mips={texture.PlatformData.Mips?.Length ?? -1} editorData={texture.EditorData is not null} sourceArt={texture.SourceArt is not null}");
                    }
                }
            }

            provider.Dispose();
            return 0;
        }

        case "open":
        {
            var request = new OpenRequest(
                Opt("profile") ?? (positional.Count > 1 ? positional[1] : null),
                Opt("dir") ?? Opt("directory"),
                Opt("game"),
                Opt("aes"),
                Opt("usmap"),
                SplitOption("paks"),
                SplitOption("tags"));
            var summary = await service.OpenAsync(request);
            if (Flag("json")) Print(summary);
            else
            {
                Console.WriteLine($"session={summary.SessionId} profile={summary.Profile}");
                Console.WriteLine($"mounted={summary.Mounted} missing={summary.Missing} files={summary.FileCount} game={summary.Game}");
                if (summary.NeedsKey) Console.Error.WriteLine("warning: some archives need AES keys; mounted part stays browsable");
            }
            return 0;
        }

        case "tree":
        {
            var sessionId = await OpenForCommand(service);
            var path = positional.Count > 1 ? positional[1] : Opt("path");
            var page = await service.BrowseAsync(sessionId, path, IntOption(10), IntOption(0, "offset"));
            if (Flag("json")) Print(page);
            else
            {
                foreach (var item in page.Items)
                {
                    if (item.IsDirectory) Console.WriteLine($"[dir] {item.Path} ({item.Count})");
                    else Console.WriteLine($"      {item.Path} ({item.Size} bytes)");
                }
                Console.WriteLine($"total={page.Total} offset={page.Offset} next={page.NextOffset?.ToString() ?? "end"}");
            }
            return 0;
        }

        case "animations":
        {
            var sessionId = await OpenForCommand(service);
            var mesh = positional.Count > 1 ? positional[1] : Opt("path");
            if (string.IsNullOrWhiteSpace(mesh)) throw AssetException.Invalid("animations requires a mesh path");
            var list = service.ListAnimations(sessionId, mesh!, Opt("prefix"), Opt("query"), IntOption(100));
            if (Flag("json")) Print(list);
            else foreach (var item in list) Console.WriteLine(item.Path);
            return 0;
        }

        case "find":
        {
            var sessionId = await OpenForCommand(service);
            var query = positional.Count > 1 ? positional[1] : Opt("query") ?? "";
            var page = await service.FindAsync(sessionId, query, Opt("prefix"), Opt("ext"), IntOption(100), IntOption(0, "offset"), Flag("all"));
            if (Flag("json")) Print(page);
            else
            {
                foreach (var item in page.Items) Console.WriteLine($"{item.Path}  ({item.Size} bytes)");
                Console.WriteLine($"total={page.Total} offset={page.Offset} next={page.NextOffset?.ToString() ?? "end"}");
            }
            return 0;
        }

        case "inspect":
        {
            var sessionId = await OpenForCommand(service);
            var path = positional.Count > 1 ? positional[1] : Opt("path");
            if (string.IsNullOrWhiteSpace(path)) throw AssetException.Invalid("inspect requires an asset path");
            var result = await service.InspectAsync(sessionId, path!, Opt("section") ?? "all", IntOption(100, "limit"));
            if (Flag("json")) Print(result);
            else
            {
                Console.WriteLine($"{result.Meta.Path}  ({result.Meta.ClassName ?? "?"}, {result.Meta.Size} bytes)");
                Console.WriteLine($"exports={result.Meta.ExportCount} imports={result.Meta.ImportCount}");
                foreach (var export in result.Exports) Console.WriteLine($"  #{export.Index} {export.Name} : {export.ClassName}");
                if (result.PropertiesJson is not null) Console.WriteLine(result.PropertiesJson);
                if (result.Note is not null) Console.Error.WriteLine("note: " + result.Note);
            }
            return 0;
        }

        case "preview":
        {
            var sessionId = await OpenForCommand(service);
            var path = positional.Count > 1 ? positional[1] : Opt("path");
            if (string.IsNullOrWhiteSpace(path)) throw AssetException.Invalid("preview requires an asset path");
            var result = await service.PreviewAsync(sessionId, path!, IntOption(1024, "max"));
            if (result.Base64 is null)
            {
                Print(result);
                return 0;
            }

            var output = Opt("out");
            if (string.IsNullOrWhiteSpace(output))
            {
                var root = service.Config.OutputRoot ?? AppConfig.DefaultOutputRoot();
                var directory = Path.Combine(root, "previews");
                Directory.CreateDirectory(directory);
                output = Path.Combine(directory, SafeName(Path.GetFileName(result.ObjectPath)) + ".png");
            }

            await File.WriteAllBytesAsync(output!, Convert.FromBase64String(result.Base64));
            Console.WriteLine(output);
            return 0;
        }

        case "mesh":
        {
            var sessionId = await OpenForCommand(service);
            var path = positional.Count > 1 ? positional[1] : Opt("path");
            if (string.IsNullOrWhiteSpace(path)) throw AssetException.Invalid("mesh requires an asset path");
            var result = await service.MeshAsync(sessionId, path!);
            if (Flag("json")) Print(result);
            else
            {
                Console.WriteLine($"glb={result.EntryFile}");
                Console.WriteLine($"url=/ue-asset/cache/{result.RelativeEntry}");
                Console.WriteLine($"bytes={result.Bytes} files={result.Files.Count} animations={result.Animations}");
            }
            return 0;
        }

        case "export":
        {
            var sessionId = await OpenForCommand(service);
            var paths = positional.Skip(1).ToList();
            if (paths.Count == 0) throw AssetException.Invalid("export requires at least one asset path");
            var jobId = service.Export(sessionId, paths);
            if (!Flag("wait") && options.ContainsKey("wait") && Opt("wait") == "false")
            {
                Console.WriteLine(jobId);
                return 0;
            }

            var final = await WaitJob(service, jobId);
            Print(final);
            return final.Status == "completed" ? 0 : 1;
        }

        case "job":
        {
            var action = positional.Count > 1 ? positional[1] : "list";
            if (action == "list")
            {
                Print(service.Jobs(Opt("session")));
                return 0;
            }
            var jobId = positional.Count > 2 ? positional[2] : Opt("id");
            if (string.IsNullOrWhiteSpace(jobId)) throw AssetException.Invalid("job requires an id");
            Print(service.Job(action, Opt("session"), jobId));
            return 0;
        }

        default:
            throw AssetException.Invalid($"unknown command: {command}");
    }
}
catch (AssetException ex)
{
    await Console.Error.WriteLineAsync($"error[{ErrorCodes.Of(ex.Kind)}]: {ex.Message}");
    return 2;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync("error[internal]: " + ex);
    return 1;
}

async Task<string> OpenForCommand(AssetService service)
{
    if (!string.IsNullOrWhiteSpace(Opt("profile")))
    {
        var summary = await service.OpenAsync(new OpenRequest(Opt("profile"), null, Opt("game"), Opt("aes"), Opt("usmap"), SplitOption("paks"), SplitOption("tags")));
        return summary.SessionId;
    }

    var directory = Opt("dir") ?? Opt("directory") ?? (positional.Count > 1 && LooksLikeDirectory(positional[1]) ? positional[1] : null);
    if (string.IsNullOrWhiteSpace(directory))
        throw AssetException.Invalid("pass --profile <name> or --dir <paks directory>");

    var opened = await service.OpenAsync(new OpenRequest(null, directory, Opt("game"), Opt("aes"), Opt("usmap"), SplitOption("paks"), SplitOption("tags")));
    return opened.SessionId;
}

string[]? SplitOption(string name)
{
    var value = Opt(name);
    if (string.IsNullOrWhiteSpace(value)) return null;
    return value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static bool LooksLikeDirectory(string value) => value.Contains('\\') || value.Contains('/') || value.Contains(':');

async Task<JobView> WaitJob(AssetService service, string jobId)
{
    while (true)
    {
        var view = service.Job("status", null, jobId) ?? throw AssetException.NotFound($"job not found: {jobId}");
        if (view.Status is "completed" or "failed" or "cancelled") return view;
        await Task.Delay(250);
    }
}

void Print(object? value) => Console.WriteLine(CliJson.Serialize(value));

int IntOption(int fallback, string name = "limit")
    => int.TryParse(Opt(name), out var parsed) ? parsed : fallback;

static string SafeName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
    return new string(chars);
}

static void PrintHelp()
{
    Console.WriteLine(@"ueasset 0.1.0 — local UE4/UE5 asset tool

Usage:
  ueasset [--config <path>] <command> [args] [options]

Commands:
  profiles                        list configured games (no secrets)
  config path|get|import-fmodel   inspect config, or import FModel AppSettings.json
  open <profile|dir>              mount one game and print diagnostics
  tree   [path]                   browse one directory level
  find   <query>                  search virtual paths
  inspect <path>                  metadata / exports / properties
  preview <path>                  texture -> PNG / mesh -> GLB / other -> properties
  mesh <path>                     export a StaticMesh/SkeletalMesh to an internal GLB
  export <path...>                export assets, wait for the job
  job    list|status|cancel [id]  inspect jobs
  doctor <profile> [asset]        mount diagnostics (pak count, keys, VFS, payload)
  serve                           newline-delimited JSON RPC on stdin/stdout

Common options:
  --config <path>   config.json (default %LOCALAPPDATA%\dsh-ue-asset\config.json)
  --profile <name>  profile to mount for this command
  --dir <path>      ad-hoc paks directory instead of a profile
  --game <EGame>    engine version override, e.g. GAME_BlackMythWukong
  --aes <hex>       ad-hoc AES key (prefer config: keys never enter chat history)
  --usmap <path>    mappings file override
  --json            machine-readable output
  --limit <n> --offset <n>
  --out <file>      preview output path
");
}