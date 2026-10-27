using DshUeAsset.Core;
using DshUeAsset.Core.Assets;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Fmodel;
using DshUeAsset.Core.Safety;
using Xunit;

namespace DshUeAsset.Tests;

public sealed class PathPolicyTests
{
    [Theory]
    [InlineData("b1/Content/Foo.uasset", "b1/Content/Foo.uasset")]
    [InlineData("/b1/Content/Foo.uasset", "b1/Content/Foo.uasset")]
    [InlineData("b1\\Content\\Foo.uasset", "b1/Content/Foo.uasset")]
    [InlineData(null, "")]
    public void NormalizesVirtualPaths(string? input, string expected)
        => Assert.Equal(expected, PathPolicy.NormalizeVirtualPath(input));

    [Fact]
    public void RejectsEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-ue-asset-tests", "root");
        Assert.Throws<AssetException>(() => PathPolicy.EnsureWithin(root, Path.Combine(root, "..", "evil")));
    }

    [Fact]
    public void AcceptsChild()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-ue-asset-tests", "root");
        var child = Path.Combine(root, "job", "a.png");
        Assert.Equal(Path.GetFullPath(child), PathPolicy.EnsureWithin(root, child));
    }

    [Theory]
    [InlineData("a.uasset", true)]
    [InlineData("a.UMAP", true)]
    [InlineData("a.uexp", false)]
    [InlineData("a.png", false)]
    public void AssetExtensions(string extension, bool expected)
        => Assert.Equal(expected, PathPolicy.IsAssetExtension(Path.GetExtension(extension)));
}

public sealed class ObjectPathTests
{
    [Theory]
    [InlineData("b1/Content/Foo.uasset", "b1/Content/Foo.uasset", null)]
    [InlineData("b1/Content/Foo.umap", "b1/Content/Foo.umap", null)]
    [InlineData("b1/Content/Foo.uasset.MyObject", "b1/Content/Foo.uasset", "MyObject")]
    [InlineData("b1/Content/Foo.Foo", "b1/Content/Foo", "Foo")]
    [InlineData("b1/Content/Foo", "b1/Content/Foo", null)]
    public void SplitsPackageAndObject(string input, string package, string? objectName)
    {
        var (actualPackage, actualObject) = ObjectReader.Split(input);
        Assert.Equal(package, actualPackage);
        Assert.Equal(objectName, actualObject);
    }
}

public sealed class ConfigStoreTests
{
    [Fact]
    public void RoundTripsCamelCaseAndSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dsh-ue-asset-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");

        try
        {
            var store = new ConfigStore(path);
            var config = new AppConfig { OutputRoot = Path.Combine(directory, "Exports"), DefaultMeshFormat = "Gltf2" };
            config.Profiles["demo"] = new GameProfile
            {
                Name = "demo",
                Directory = directory,
                Game = "GAME_UE5_0",
                AesKey = "0x" + new string('A', 64)
            };
            store.Save(config);

            var json = File.ReadAllText(path);
            Assert.Contains("\"profiles\"", json);
            Assert.Contains("\"aesKey\"", json);

            var loaded = store.Load();
            Assert.Equal("Gltf2", loaded.DefaultMeshFormat);
            Assert.Equal("demo", Assert.Single(loaded.Profiles).Key);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

public sealed class FmodelImporterTests
{
    [Fact]
    public void ResolvesContentPaksUnderGameRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-ue-asset-tests", Guid.NewGuid().ToString("N"));
        var paks = Path.Combine(root, "Content", "Paks");
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "pakchunk0-Windows.pak"), "");

        try
        {
            Assert.Equal(paks, FmodelImporter.ResolvePaksDirectory(root));
            Assert.Equal(paks, FmodelImporter.ResolvePaksDirectory(paks));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}