using CUE4Parse.FileProvider;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using DshUeAsset.Core.Assets;
using DshUeAsset.Core.Models;
using DshUeAsset.Core.Safety;

namespace DshUeAsset.Core.Export;

public static class ExportService
{
    /// <summary>
    /// ExportSession.RunAsync(baseDirectory, ...) resolves every output path — including
    /// referenced textures/materials pulled in as dependencies — beneath baseDirectory. That is
    /// stronger than the post-hoc staging dance older CUE4Parse versions needed, so the job
    /// directory is both the sandbox and the destination.
    /// </summary>
    public static async Task<(IReadOnlyList<ExportFileView> Results, string OutputDirectory)> RunAsync(
        IFileProvider provider,
        IReadOnlyList<string> paths,
        string outputRoot,
        string meshFormat,
        int maxBatch,
        JobRecord job,
        CancellationToken cancellationToken)
    {
        if (paths.Count == 0) throw AssetException.Invalid("no asset paths supplied");
        if (paths.Count > maxBatch) throw AssetException.TooLarge($"too many assets in one job: {paths.Count} (max {maxBatch})");

        var jobDirectory = Path.Combine(
            Path.GetFullPath(outputRoot),
            "jobs",
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(jobDirectory);
        job.OutputDirectory = jobDirectory;

        var results = new List<ExportFileView>();
        var session = new ExportSession
        {
            // The provider is not thread safe: dependency resolution during export must not run
            // in parallel even though ExportSession supports it.
            MaxDegreeOfParallelism = 1
        };

        var queued = 0;
        foreach (var requested in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (packagePath, objectName) = ObjectReader.Split(requested);
            packagePath = AssetQuery.ResolveVirtualPath(provider, packagePath);
            try
            {
                var package = provider.LoadPackage(packagePath);
                var exports = package.GetExports().ToList();
                if (objectName is not null)
                    exports = exports.Where(e => e.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (exports.Count == 0)
                {
                    results.Add(new ExportFileView(requested, false, [], "no matching export in package"));
                    continue;
                }

                var added = 0;
                foreach (var export in exports)
                {
                    try
                    {
                        session.Add(export);
                        added++;
                    }
                    catch (NotSupportedException)
                    {
                        // Blueprints/data tables have no exporter; report them instead of failing the batch.
                    }
                }

                if (added == 0)
                    results.Add(new ExportFileView(requested, false, [], "no supported exporter for this asset type"));
                else
                    queued++;
            }
            catch (Exception ex)
            {
                results.Add(new ExportFileView(requested, false, [], ex.Message));
            }
        }

        if (queued == 0)
        {
            job.Completed = job.Total = paths.Count;
            return (results, jobDirectory);
        }

        job.Total = queued;
        job.Completed = 0;
        job.Message = "exporting";

        var progress = new Progress<ExportProgress>(p =>
        {
            job.Completed = p.Completed;
            job.Total = Math.Max(p.Total, 1);
            job.UpdatedAt = DateTimeOffset.Now;
        });

        var exported = await session.RunAsync(jobDirectory, BuildOptions(meshFormat), progress, cancellationToken)
            .ConfigureAwait(false);

        foreach (var result in exported)
        {
            var files = new List<string>();
            foreach (var diskPath in result.DiskFilePaths ?? [])
            {
                // Defense in depth: a future CUE4Parse change must not write outside the job dir.
                var safe = PathPolicy.EnsureWithin(jobDirectory, diskPath);
                files.Add(safe);
            }

            results.Add(new ExportFileView(
                result.ObjectPath,
                result.Success,
                files,
                result.Error?.Message));
        }

        return (results, jobDirectory);
    }

    public static ExportOptions BuildOptions(string? meshFormat)
    {
        var format = (meshFormat ?? "UEFormat").Trim().ToLowerInvariant() switch
        {
            "gltf" or "gltf2" => EMeshFormat.Gltf2,
            "usd" => EMeshFormat.USD,
            "actorx" => EMeshFormat.ActorX,
            _ => EMeshFormat.UEFormat
        };

        return new ExportOptions(
            meshFormat: format,
            texturePlatform: CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile,
            textureFormat: ETextureFormat.Png,
            exportMaterials: true);
    }
}