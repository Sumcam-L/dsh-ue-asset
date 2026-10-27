using DshUeAsset.Core.Config;

namespace DshUeAsset.Core.Sessions;

public sealed class SessionManager
{
    private readonly object _lock = new();
    private readonly Dictionary<string, GameSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byDirectory = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppConfig _config;

    public SessionManager(AppConfig config)
    {
        _config = config;
    }

    public IReadOnlyList<GameSession> All
    {
        get { lock (_lock) return _sessions.Values.ToList(); }
    }

    public GameSession Get(string sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw AssetException.NotFound($"session not found: {sessionId}");
            return session;
        }
    }

    public GameSession? TryGet(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        lock (_lock) return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public GameSession Open(GameProfile profile, IReadOnlyList<DshUeAsset.Core.Models.PakInfo>? selection = null)
    {
        var pakNames = (selection ?? []).Select(p => p.Name).ToList();
        var key = profile.Key + "|" + string.Join(",", pakNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        lock (_lock)
        {
            if (_byDirectory.TryGetValue(key, out var existingId) && _sessions.TryGetValue(existingId, out var existing))
            {
                // Re-opening the same directory with changed mount-level settings must rebuild
                // the provider; otherwise a settings-page edit silently keeps the old AES key /
                // usmap / engine version until the app restarts.
                if (NeedsRemount(existing.Profile, profile) || !existing.Paks.SequenceEqual(pakNames, StringComparer.OrdinalIgnoreCase))
                {
                    if (existing.Jobs.RunningCount > 0)
                        throw AssetException.Invalid("cannot reload a profile while export jobs are running", new { profile = profile.Name });
                    _sessions.Remove(existingId);
                    _byDirectory.Remove(key);
                    existing.Dispose();
                }
                else
                {
                    existing.Touch();
                    return existing;
                }
            }

            if (_sessions.Count >= Math.Max(1, _config.Limits.MaxSessions))
                EvictOneLocked();

            var id = Guid.NewGuid().ToString("N")[..8];
            var (provider, report) = ProviderFactory.Create(profile, _config.OodleLibrary, selection);
            var session = new GameSession(id, profile.Clone(), provider, report, pakNames);
            _sessions[id] = session;
            _byDirectory[key] = id;
            _config.UiState.LastProfile = profile.Name;
            return session;
        }
    }

    public void Close(string sessionId)
    {
        GameSession session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session!))
                throw AssetException.NotFound($"session not found: {sessionId}");
            if (session.Jobs.RunningCount > 0)
                throw AssetException.Invalid("cannot close a session while export jobs are running", new { sessionId });
            _sessions.Remove(sessionId);
            _byDirectory.Remove(session.Profile.Key);
        }

        session.Dispose();
    }

    public void CloseAll()
    {
        List<GameSession> all;
        lock (_lock)
        {
            all = _sessions.Values.ToList();
            _sessions.Clear();
            _byDirectory.Clear();
        }

        foreach (var session in all) session.Dispose();
    }

    private static bool NeedsRemount(GameProfile current, GameProfile requested)
    {
        if (!string.Equals(current.Directory, requested.Directory, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(current.Game, requested.Game, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(current.Platform, requested.Platform, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(current.AesKey ?? "", requested.AesKey ?? "", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(current.Usmap ?? "", requested.Usmap ?? "", StringComparison.OrdinalIgnoreCase)) return true;

        if (current.ExtraDirectories.Count != requested.ExtraDirectories.Count) return true;
        for (var i = 0; i < current.ExtraDirectories.Count; i++)
        {
            if (!string.Equals(current.ExtraDirectories[i], requested.ExtraDirectories[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (current.DynamicKeys.Count != requested.DynamicKeys.Count) return true;
        for (var i = 0; i < current.DynamicKeys.Count; i++)
        {
            if (!string.Equals(current.DynamicKeys[i].Guid, requested.DynamicKeys[i].Guid, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(current.DynamicKeys[i].Key, requested.DynamicKeys[i].Key, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private void EvictOneLocked()
    {
        var candidate = _sessions.Values
            .Where(s => s.Jobs.RunningCount == 0)
            .OrderBy(s => s.LastUsedAt)
            .FirstOrDefault();

        if (candidate is null)
            throw AssetException.Invalid("session limit reached and every session has a running job", new { limit = _config.Limits.MaxSessions });

        _sessions.Remove(candidate.Id);
        _byDirectory.Remove(candidate.Profile.Key);
        candidate.Dispose();
    }
}