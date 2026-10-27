using DshUeAsset.Core.Config;
using DshUeAsset.Core.Paks;
using Xunit;

namespace DshUeAsset.Tests;

public sealed class PakFingerprintTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dsh-ue-asset-fp-" + Guid.NewGuid().ToString("N"));

    public PakFingerprintTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Fresh_index_is_not_stale()
    {
        var profile = Profile();
        var pak = WritePak("pakchunk0-Windows.pak");

        var fingerprint = PakFingerprint.Compute(profile, ["pakchunk0-Windows.pak"]);
        var (stale, reason) = PakFingerprint.Compare(profile, ["pakchunk0-Windows.pak"], fingerprint);

        Assert.False(stale);
        Assert.Null(reason);
        Assert.False(string.IsNullOrWhiteSpace(fingerprint));
    }

    [Fact]
    public void Touched_pak_makes_index_stale()
    {
        var profile = Profile();
        var pak = WritePak("pakchunk0-Windows.pak");
        var fingerprint = PakFingerprint.Compute(profile, ["pakchunk0-Windows.pak"]);

        File.SetLastWriteTimeUtc(pak, DateTime.UtcNow.AddHours(1));

        var (stale, reason) = PakFingerprint.Compare(profile, ["pakchunk0-Windows.pak"], fingerprint);
        Assert.True(stale);
        Assert.Contains("pak changed", reason);
    }

    [Fact]
    public void Missing_pak_makes_index_stale()
    {
        var profile = Profile();
        WritePak("pakchunk0-Windows.pak");
        var fingerprint = PakFingerprint.Compute(profile, ["pakchunk0-Windows.pak"]);
        File.Delete(Path.Combine(_directory, "pakchunk0-Windows.pak"));

        var (stale, reason) = PakFingerprint.Compare(profile, ["pakchunk0-Windows.pak"], fingerprint);
        Assert.True(stale);
        Assert.Contains("pak missing", reason);
    }

    private GameProfile Profile() => new() { Name = "t", Directory = _directory };

    private string WritePak(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "not-a-real-pak");
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }
}
