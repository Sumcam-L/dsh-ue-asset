using System.Text;
using Newtonsoft.Json;

namespace DshUeAsset.Core.Config;

/// <summary>
/// Every on-disk location the tool owns, in one place. Data lives under the DSH home
/// (`%DSH_HOME%` when set, otherwise `%USERPROFILE%\.dsh`), not under LOCALAPPDATA, so the
/// plugin's config and cache sit next to the rest of the harness state.
/// </summary>
public static class AppPaths
{
    private const string FolderName = "dsh-ue-asset";

    /// <summary>DSH home: `$DSH_HOME`, else `%USERPROFILE%\.dsh`.</summary>
    public static string DshHome()
    {
        var configured = Environment.GetEnvironmentVariable("DSH_HOME");
        if (!string.IsNullOrWhiteSpace(configured)) return Normalize(configured!);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".dsh");
    }

    /// <summary>Root of everything this tool writes under the DSH home.</summary>
    public static string DataRoot() => Path.Combine(DshHome(), FolderName);

    public static string ConfigPath() => Path.Combine(DataRoot(), "config.json");

    public static string CacheRoot() => Path.Combine(DataRoot(), "cache");

    public static string OutputRoot() => Path.Combine(DataRoot(), "Exports");

    /// <summary>
    /// Pre-0.1.10 location. Kept only so startup can migrate from it. Resolved through the
    /// environment variable rather than SpecialFolder.LocalApplicationData so the migration can be
    /// exercised against a scratch directory (and so a redirected LOCALAPPDATA is honoured).
    /// </summary>
    public static string LegacyRoot()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local))
            local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(Normalize(local!), FolderName);
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
        return Path.TrimEndingDirectorySeparator(full);
    }
}

/// <summary>Result of a one-shot move of the legacy LOCALAPPDATA tree into the DSH home.</summary>
public sealed record MigrationOutcome(
    bool Migrated,
    string? From,
    string? To,
    IReadOnlyList<string> Moved,
    IReadOnlyList<string> Skipped,
    string? Reason);

/// <summary>
/// Moves the pre-0.1.10 `%LOCALAPPDATA%\dsh-ue-asset` tree into `%DSH_HOME%\dsh-ue-asset`.
/// Runs once at startup, is idempotent, and never overwrites data that is already in the new
/// location: a file that exists on both sides keeps the new copy and reports the old one as skipped.
/// </summary>
public static class AppMigration
{
    private const string MarkerName = ".migrated";

    public static MigrationOutcome Ensure()
    {
        var from = AppPaths.LegacyRoot();
        var to = AppPaths.DataRoot();
        var marker = Path.Combine(to, MarkerName);

        if (!Directory.Exists(from))
            return new MigrationOutcome(false, null, null, [], [], "no legacy directory");
        if (PathEquals(from, to))
            return new MigrationOutcome(false, from, to, [], [], "legacy and current locations are the same");
        if (File.Exists(marker))
            return new MigrationOutcome(false, from, to, [], [], "already migrated");

        var moved = new List<string>();
        var skipped = new List<string>();
        try
        {
            Directory.CreateDirectory(to);

            // config.json first: once it is in place the cache path it points at is rewritten below.
            var legacyConfig = Path.Combine(from, "config.json");
            var targetConfig = Path.Combine(to, "config.json");
            var configMoved = false;
            if (File.Exists(legacyConfig))
            {
                if (File.Exists(targetConfig))
                {
                    skipped.Add("config.json (already present in " + to + ")");
                }
                else
                {
                    File.Move(legacyConfig, targetConfig);
                    moved.Add("config.json");
                    configMoved = true;
                }
            }

            // Everything else: cache/, an Oodle DLL, stray exports.
            foreach (var entry in Directory.EnumerateFileSystemEntries(from))
            {
                var name = Path.GetFileName(entry);
                if (name.Equals("config.json", StringComparison.OrdinalIgnoreCase)) continue;

                var target = Path.Combine(to, name);
                if (Directory.Exists(entry))
                {
                    if (Directory.Exists(target))
                        MoveDirectoryContents(entry, target, moved, skipped);
                    else
                    {
                        try
                        {
                            Directory.Move(entry, target);
                            moved.Add(name + "/");
                        }
                        catch (IOException)
                        {
                            // Cross-volume move: fall back to a recursive copy + delete.
                            MoveDirectoryContents(entry, target, moved, skipped);
                        }
                    }
                }
                else if (File.Exists(entry))
                {
                    if (File.Exists(target)) skipped.Add(name + " (already present)");
                    else
                    {
                        try
                        {
                            File.Move(entry, target);
                            moved.Add(name);
                        }
                        catch (IOException)
                        {
                            File.Copy(entry, target, overwrite: false);
                            File.Delete(entry);
                            moved.Add(name);
                        }
                    }
                }
            }

            if (configMoved) RetargetCacheRoot(targetConfig);

            // Whatever is left in the legacy directory is a duplicate the new location already
            // owns (an entry we skipped on purpose) or something we could not touch. Either way the
            // marker goes down now: retrying on every launch would report the same thing forever
            // and could never make progress. The leftover stays put and is reported instead.
            var keptAnything = HasRemainingEntries(from);
            if (!keptAnything) TryDeleteDirectory(from);
            File.WriteAllText(marker, JsonConvert.SerializeObject(new
            {
                migratedAt = DateTimeOffset.Now,
                from,
                moved,
                skipped,
                keptLegacyDirectory = keptAnything
            }));

            return new MigrationOutcome(
                true,
                from,
                to,
                moved,
                skipped,
                keptAnything ? "legacy directory kept (duplicates left in place)" : null);
        }
        catch (Exception ex)
        {
            // No marker: the next startup retries from wherever this attempt stopped.
            return new MigrationOutcome(false, from, to, moved, skipped, ex.Message);
        }
    }

    /// <summary>
    /// Migration must not leave a config pointing at the old cache. The explicit value written by the
    /// old default is dropped so ResolveCacheRoot falls back to the new default.
    /// </summary>
    private static void RetargetCacheRoot(string configPath)
    {
        try
        {
            var json = File.ReadAllText(configPath, Encoding.UTF8);
            var config = JsonConvert.DeserializeObject<AppConfig>(json);
            if (config is null) return;

            var legacy = AppPaths.LegacyRoot();
            var changed = false;
            if (!string.IsNullOrWhiteSpace(config.CacheRoot) &&
                IsUnder(legacy, config.CacheRoot!))
            {
                config.CacheRoot = null;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(config.OutputRoot) && IsUnder(legacy, config.OutputRoot!))
            {
                config.OutputRoot = null;
                changed = true;
            }
            if (!changed) return;

            File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
        }
        catch
        {
            // A malformed old config is still loadable by ConfigStore; never fail startup over this.
        }
    }

    private static void MoveDirectoryContents(string source, string target, List<string> moved, List<string> skipped)
    {
        Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            var destination = Path.Combine(target, name);
            if (Directory.Exists(entry))
            {
                if (Directory.Exists(destination)) MoveDirectoryContents(entry, destination, moved, skipped);
                else
                {
                    Directory.Move(entry, destination);
                    moved.Add(name + "/");
                }
            }
            else if (File.Exists(destination)) skipped.Add(name);
            else
            {
                try
                {
                    File.Move(entry, destination);
                }
                catch (IOException)
                {
                    File.Copy(entry, destination, overwrite: false);
                    File.Delete(entry);
                }
                moved.Add(name);
            }
        }

        if (!HasRemainingEntries(source)) TryDeleteDirectory(source);
    }

    private static bool HasRemainingEntries(string directory)
    {
        try
        {
            return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
        }
        catch
        {
            return true;
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !HasRemainingEntries(directory)) Directory.Delete(directory);
        }
        catch
        {
            // Stale directories are harmless; the marker still prevents re-running the move.
        }
    }

    private static bool IsUnder(string root, string candidate)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidateFull = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(candidateFull, rootFull, comparison) ||
               candidateFull.StartsWith(rootFull + Path.DirectorySeparatorChar, comparison);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
