using CUE4Parse.FileProvider;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Export;
using DshUeAsset.Core.Models;

namespace DshUeAsset.Core.Sessions;

public sealed class GameSession : IDisposable
{
    private bool _disposed;

    public GameSession(string id, GameProfile profile, IFileProvider provider, MountReport report, IReadOnlyList<string> paks)
    {
        Id = id;
        Profile = profile;
        Provider = provider;
        Report = report;
        Paks = paks;
        Jobs = new JobQueue(profile.Name);
        CreatedAt = DateTimeOffset.Now;
        LastUsedAt = CreatedAt;
    }

    public string Id { get; }
    public GameProfile Profile { get; }
    public IFileProvider Provider { get; }
    public MountReport Report { get; private set; }

    /// <summary>A late AES key mounts more archives; the summary must reflect the new state.</summary>
    public void UpdateReport(MountReport report) => Report = report;
    public IReadOnlyList<string> Paks { get; }
    public JobQueue Jobs { get; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }

    /// <summary>
    /// CUE4Parse providers are not thread safe: LoadPackage mutates internal dictionaries, and
    /// concurrent reads produce rare, hard-to-reproduce KeyNotFoundExceptions. Every parse goes
    /// through this gate. Job status queries deliberately bypass it.
    /// </summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public void Touch() => LastUsedAt = DateTimeOffset.Now;

    public SessionSummary ToSummary() => new(
        Id,
        Profile.Name,
        Profile.Directory,
        Profile.Game,
        Profile.Platform,
        Report.Mounted,
        Report.MissingKeys.Count,
        Report.NeedsKey,
        Report.FileCount,
        CreatedAt,
        LastUsedAt,
        Jobs.RunningCount,
        Paks);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Gate.Dispose();
        try { Provider.Dispose(); } catch { /* provider teardown is best effort */ }
    }
}