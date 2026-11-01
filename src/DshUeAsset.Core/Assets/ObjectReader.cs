using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using DshUeAsset.Core.Models;
using DshUeAsset.Core.Safety;
using Newtonsoft.Json;

namespace DshUeAsset.Core.Assets;

public static class ObjectReader
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        MaxDepth = 32,
        DateParseHandling = DateParseHandling.None
    };

    public static InspectResult Inspect(IFileProvider provider, string objectPath, string section, int maxChars, int exportLimit)
    {
        var (packagePath, objectName) = Split(objectPath);
        packagePath = AssetQuery.ResolveVirtualPath(provider, packagePath);
        var file = AssetQuery.FindFile(provider, packagePath);
        if (file is null)
            throw AssetException.NotFound($"asset not found: {packagePath}", new { path = packagePath });

        IPackage package;
        try
        {
            package = provider.LoadPackage(packagePath);
        }
        catch (Exception ex)
        {
            throw new AssetException(AssetErrorKind.ParserError, $"failed to parse package: {ex.Message}", new { path = packagePath }, ex);
        }

        var exports = package.GetExports().ToList();
        var summary = exports
            .Select((e, i) => new ExportSummary(i, Safe(() => e.Name), Safe(() => e.ExportType)))
            .Take(Math.Clamp(exportLimit, 1, 500))
            .ToList();

        var main = SelectMainExport(exports, packagePath, objectName);

        var meta = new ExportMeta(
            packagePath,
            file.IsUePackage,
            file.Size,
            file.Extension,
            main is null ? null : Safe(() => main.ExportType),
            package.ExportMapLength,
            package.ImportMapLength);

        string? json = null;
        var truncated = false;
        if (section is "properties" or "all")
        {
            if (main is null)
            {
                json = "{}";
            }
            else
            {
                try
                {
                    json = JsonConvert.SerializeObject(main, Settings);
                    if (maxChars > 0 && json.Length > maxChars)
                    {
                        json = json[..maxChars] + "\n… (truncated)";
                        truncated = true;
                    }
                }
                catch (Exception ex)
                {
                    json = JsonConvert.SerializeObject(new { error = ex.Message }, Formatting.Indented);
                }
            }
        }

        var note = objectName is not null && main is null ? $"object '{objectName}' not found in package; showing metadata only" : null;
        return new InspectResult(meta, summary, json, truncated, note);
    }

    public static UObject Load(IFileProvider provider, string objectPath)
    {
        var (packagePath, objectName) = Split(objectPath);
        packagePath = AssetQuery.ResolveVirtualPath(provider, packagePath);
        try
        {
            // LoadPackageObject(path) treats the substring after the last dot as the export name,
            // so "Foo.uasset" would look for an export called "uasset". Loading the package and
            // picking the export ourselves avoids that ambiguity.
            var package = provider.LoadPackage(packagePath);
            var exports = package.GetExports().ToList();
            return SelectMainExport(exports, packagePath, objectName)
                   ?? throw new AssetException(AssetErrorKind.NotFound, $"package has no matching export: {packagePath}", new { path = packagePath, objectName });
        }
        catch (AssetException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AssetException(AssetErrorKind.ParserError, $"failed to load object: {ex.Message}", new { path = objectPath }, ex);
        }
    }

    /// <summary>
    /// A cooked package can contain many exports (cloth assets, sockets, the mesh itself).
    /// Picking exports[0] made SK_*_body packages preview as ClothConfigNv. Prefer an export
    /// whose name matches the package leaf, then the first renderable export, then the first.
    /// </summary>
    public static UObject? SelectMainExport(IReadOnlyList<UObject> exports, string packagePath, string? objectName)
    {
        if (exports.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(objectName))
        {
            return exports.FirstOrDefault(e => e.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase));
        }

        var leaf = packagePath.Replace('\\', '/').Substring(packagePath.Replace('\\', '/').LastIndexOf('/') + 1);
        var byName = exports.FirstOrDefault(e => e.Name.Equals(leaf, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;

        var byType = exports.FirstOrDefault(e => e.ExportType is
            "SkeletalMesh" or "StaticMesh" or "Texture2D" or "TextureCube" or "Material" or
            "MaterialInstanceConstant" or "AnimSequence" or "AnimMontage" or "Skeleton" or "World");
        return byType ?? exports[0];
    }

    public static (string PackagePath, string? ObjectName) Split(string objectPath)
    {
        var value = PathPolicy.NormalizeVirtualPath(objectPath);
        var dot = value.LastIndexOf('.');
        var slash = value.LastIndexOf('/');
        if (dot <= slash || dot <= 0 || dot >= value.Length - 1) return (value, null);

        var suffix = value[(dot + 1)..];
        // "Foo.uasset" is a package path, not "package Foo + object uasset".
        if (suffix.Equals("uasset", StringComparison.OrdinalIgnoreCase) ||
            suffix.Equals("umap", StringComparison.OrdinalIgnoreCase))
            return (value, null);

        return (value[..dot], suffix);
    }

    private static string Safe(Func<string> read)
    {
        try { return read() ?? ""; } catch { return ""; }
    }
}