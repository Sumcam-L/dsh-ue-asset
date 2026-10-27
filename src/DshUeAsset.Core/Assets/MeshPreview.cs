using System.Security.Cryptography;
using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using DshUeAsset.Core.Models;
using DshUeAsset.Core.Safety;
using Newtonsoft.Json.Linq;

namespace DshUeAsset.Core.Assets;

/// <summary>
/// Exports a StaticMesh / SkeletalMesh to a self-contained GLB under an internal cache.
/// The cache is never the user-facing export root: preview artifacts are disposable.
/// </summary>
public static class MeshPreview
{
    private static readonly HashSet<string> MeshTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "StaticMesh", "SkeletalMesh"
    };

    public static bool IsMesh(UObject value) => MeshTypes.Contains(value.ExportType);

    public static async Task<MeshPreviewResult> ExportAsync(
        IFileProvider provider,
        string objectPath,
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        var (packagePath, _) = ObjectReader.Split(objectPath);
        packagePath = AssetQuery.ResolveVirtualPath(provider, packagePath);
        var file = AssetQuery.FindFile(provider, packagePath)
                   ?? throw AssetException.NotFound($"asset not found: {packagePath}", new { path = packagePath });

        var export = ObjectReader.Load(provider, objectPath);
        if (!IsMesh(export))
            throw AssetException.Unsupported($"'{export.ExportType}' is not a mesh", new { path = objectPath, className = export.ExportType });

        // Cache identity includes the archive size so a patched game invalidates old GLBs.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{provider.Versions.Game}|{packagePath}|{file.Size}|{export.ExportType}")))[..20].ToLowerInvariant();
        var cacheDirectory = Path.Combine(cacheRoot, key);
        Directory.CreateDirectory(cacheDirectory);

        var noMaterialsMarker = Path.Combine(cacheDirectory, ".nomaterials");
        var cached = FindEntry(cacheDirectory);
        if (cached is not null)
            return Describe(cacheDirectory, cached, export.ExportType, cacheRoot, materials: !File.Exists(noMaterialsMarker));

        // Pass 1: materials + textures. Some games reference stub/undecodable textures
        // (SizeX=0) and CUE4Parse aborts the whole mesh export when one of them fails.
        // Pass 2 drops materials so the geometry is still previewable.
        var materials = true;
        var results = await RunExportAsync(export, cacheDirectory, materials, cancellationToken).ConfigureAwait(false);
        if (results.Any(r => !r.Success) || FindEntry(cacheDirectory) is null)
        {
            ClearDirectory(cacheDirectory);
            materials = false;
            results = await RunExportAsync(export, cacheDirectory, materials, cancellationToken).ConfigureAwait(false);
            if (results.Any(r => !r.Success) || FindEntry(cacheDirectory) is null)
            {
                var error = results.FirstOrDefault(r => !r.Success)?.Error?.Message ?? "produced no .glb/.gltf";
                throw new AssetException(AssetErrorKind.ParserError, $"mesh export failed: {error}", new { path = objectPath });
            }
            File.WriteAllText(noMaterialsMarker, "1");
        }

        var entry = FindEntry(cacheDirectory)
                    ?? throw new AssetException(AssetErrorKind.ParserError, "mesh export produced no .glb/.gltf", new { path = objectPath });

        return Describe(cacheDirectory, entry, export.ExportType, cacheRoot, materials);
    }

    private static string? FindEntry(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "*.glb", SearchOption.AllDirectories).FirstOrDefault()
               ?? Directory.EnumerateFiles(directory, "*.gltf", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static async Task<IReadOnlyList<CUE4Parse_Conversion.ExportResult>> RunExportAsync(UObject export, string directory, bool materials, CancellationToken ct)
    {
        var session = new ExportSession { MaxDegreeOfParallelism = 1 };
        session.Add(export);
        var options = new ExportOptions(
            meshFormat: EMeshFormat.Gltf2,
            texturePlatform: CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile,
            textureFormat: ETextureFormat.Png,
            exportMaterials: materials,
            exportMorphTargets: false);
        return await session.RunAsync(directory, options, null, ct).ConfigureAwait(false);
    }

    private static void ClearDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.Delete(file);
        foreach (var dir in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            Directory.Delete(dir, true);
    }

    private static MeshPreviewResult Describe(string cacheDirectory, string entryFile, string className, string cacheRoot, bool materials)
    {
        var full = Path.GetFullPath(entryFile);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        var files = Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToList();
        var bytes = new FileInfo(full).Length;

        return new MeshPreviewResult(
            Path.GetRelativePath(root, cacheDirectory).Replace('\\', '/'),
            full,
            relative,
            className,
            bytes,
            files,
            Animations: full.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && HasAnimations(full),
            Materials: materials,
            MaterialBindings: CollectMaterialBindings(cacheDirectory, root));
    }

    /// <summary>
    /// CUE4Parse's glTF exporter writes material JSON + PNG files but does not always link the
    /// textures into the GLB. Rebuild the material -> base-color map from those files so the
    /// viewer can attach diffuse maps by material name.
    /// </summary>
    private static IReadOnlyList<MeshMaterialBinding> CollectMaterialBindings(string cacheDirectory, string cacheRoot)
    {
        var pngs = Directory.EnumerateFiles(cacheDirectory, "*.png", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var bindings = new List<MeshMaterialBinding>();
        foreach (var jsonPath in Directory.EnumerateFiles(cacheDirectory, "*.json", SearchOption.AllDirectories))
        {
            JObject? root;
            try { root = JObject.Parse(File.ReadAllText(jsonPath)); }
            catch { continue; }

            var textures = root["Textures"] as JObject;
            if (textures is null) continue;

            string? Pick(params string[] preferred)
            {
                foreach (var key in preferred)
                {
                    if (textures[key]?["ObjectName"]?.Value<string>() is { } objectName)
                    {
                        var name = objectName.Contains('\'') ? objectName.Split('\'')[1] : objectName;
                        if (pngs.TryGetValue(name, out var png))
                            return Path.GetRelativePath(cacheRoot, png).Replace('\\', '/');
                    }
                }
                return null;
            }

            var baseColor = Pick("BaseColor+DetailMask", "PM_Diffuse", "BaseColor", "Diffuse", "Albedo", "T_Body_D");
            if (baseColor is null)
            {
                foreach (var entry in textures.Properties())
                {
                    var objectName = entry.Value?["ObjectName"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(objectName)) continue;
                    var name = objectName.Contains('\'') ? objectName.Split('\'')[1] : objectName;
                    if (name.EndsWith("_D", StringComparison.OrdinalIgnoreCase) && pngs.TryGetValue(name, out var png))
                    {
                        baseColor = Path.GetRelativePath(cacheRoot, png).Replace('\\', '/');
                        break;
                    }
                }
            }

            if (baseColor is not null)
                bindings.Add(new MeshMaterialBinding(Path.GetFileNameWithoutExtension(jsonPath), baseColor));
        }

        return bindings;
    }

    private static bool HasAnimations(string glbPath)
    {
        try
        {
            using var stream = File.OpenRead(glbPath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x46546C67) return false;
            reader.ReadUInt32();
            reader.ReadUInt32();
            var chunkLength = reader.ReadUInt32();
            reader.ReadUInt32();
            var jsonBytes = reader.ReadBytes((int)Math.Min(chunkLength, 8 * 1024 * 1024));
            var json = Encoding.UTF8.GetString(jsonBytes);
            return json.Contains("\"animations\"", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}