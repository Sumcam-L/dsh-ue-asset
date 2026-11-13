using DshUeAsset.Core.Config;
using Xunit;

namespace DshUeAsset.Tests;

/// <summary>
/// Exercises the startup migration against real throwaway directories. Both roots are driven by
/// environment variables (DSH_HOME and LOCALAPPDATA), so the tests never touch the developer's
/// own config or cache.
/// </summary>
public sealed class AppMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly string? _dshHome;
    private readonly string? _localAppData;

    public AppMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dsh-ue-asset-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dshHome = Environment.GetEnvironmentVariable("DSH_HOME");
        _localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("DSH_HOME", Path.Combine(_root, "home"));
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(_root, "local"));
    }

    private string Legacy => Path.Combine(_root, "local", "dsh-ue-asset");
    private string Current => Path.Combine(_root, "home", "dsh-ue-asset");

    [Fact]
    public void MovesConfigCacheAndOodleIntoTheDshHome()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "cache", "abc"));
        File.WriteAllText(Path.Combine(Legacy, "config.json"),
            "{\"cacheRoot\":\"" + Path.Combine(Legacy, "cache").Replace("\\", "/") + "\",\"profiles\":{\"demo\":{\"name\":\"demo\"}}}");
        File.WriteAllBytes(Path.Combine(Legacy, "cache", "abc", "model.glb"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(Legacy, "oo2core_9_win64.dll"), new byte[128]);

        var outcome = AppMigration.Ensure();

        Assert.True(outcome.Migrated);
        Assert.True(File.Exists(Path.Combine(Current, "config.json")));
        Assert.True(File.Exists(Path.Combine(Current, "cache", "abc", "model.glb")));
        Assert.True(File.Exists(Path.Combine(Current, "oo2core_9_win64.dll")));
        Assert.False(Directory.Exists(Legacy));

        // The old config pointed its cache at the legacy tree; it must fall back to the new default.
        var config = new ConfigStore(Path.Combine(Current, "config.json")).Load();
        Assert.Null(config.CacheRoot);
        Assert.Equal(Path.Combine(Current, "cache"), config.ResolveCacheRoot());
    }

    [Fact]
    public void IsIdempotentAndNeverOverwritesNewData()
    {
        Directory.CreateDirectory(Current);
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Current, "config.json"), "{\"defaultMeshFormat\":\"USD\"}");
        File.WriteAllText(Path.Combine(Legacy, "config.json"), "{\"defaultMeshFormat\":\"Gltf2\"}");
        File.WriteAllText(Path.Combine(Legacy, "keep.txt"), "legacy");

        var first = AppMigration.Ensure();
        Assert.True(first.Migrated);
        Assert.Contains(first.Skipped, item => item.StartsWith("config.json"));
        Assert.Equal("USD", new ConfigStore(Path.Combine(Current, "config.json")).Load().DefaultMeshFormat);

        var second = AppMigration.Ensure();
        Assert.False(second.Migrated);
        Assert.Equal("already migrated", second.Reason);
    }

    [Fact]
    public void NoLegacyDirectoryIsANoOp()
    {
        var outcome = AppMigration.Ensure();
        Assert.False(outcome.Migrated);
        Assert.Equal("no legacy directory", outcome.Reason);
        Assert.False(Directory.Exists(Current));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DSH_HOME", _dshHome);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _localAppData);
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Temp cleanup is best effort.
        }
    }
}

public sealed class CacheManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-ue-asset-tests", Guid.NewGuid().ToString("N"));

    public CacheManagerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "index"));
        Directory.CreateDirectory(Path.Combine(_root, "abc"));
        File.WriteAllBytes(Path.Combine(_root, "index", "demo.db"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(_root, "abc", "model.glb"), new byte[512]);
    }

    [Fact]
    public void MeasuresEverySubtree()
    {
        var stats = CacheManager.Stats(_root);
        Assert.Equal(1536, stats.TotalBytes);
        Assert.Equal(2, stats.TotalFiles);
        Assert.Equal(2, stats.Parts.Count);
        Assert.Contains(stats.Parts, part => part.Name == "index" && part.Bytes == 1024);
    }

    [Fact]
    public void ClearRemovesEverythingAndKeepsTheRoot()
    {
        var cleared = CacheManager.Clear(_root);
        Assert.Equal(1536, cleared.TotalBytes);
        Assert.True(Directory.Exists(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
        Assert.Equal(0, CacheManager.Stats(_root).TotalBytes);
    }

    [Fact]
    public void MissingRootReportsZero()
    {
        var stats = CacheManager.Stats(Path.Combine(_root, "nope"));
        Assert.Equal(0, stats.TotalBytes);
        Assert.Empty(stats.Parts);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Temp cleanup is best effort.
        }
    }
}
