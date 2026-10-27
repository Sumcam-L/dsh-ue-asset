using CUE4Parse.FileProvider;
using DshUeAsset.Core.Config;
using DshUeAsset.Core.Paks;
using Microsoft.Data.Sqlite;

namespace DshUeAsset.Core.Index;

public sealed record IndexEntry(string Path, string Name, string Ext, string Kind, long Size);

public sealed record IndexStatus(
    string Profile,
    string DbPath,
    bool Exists,
    int Count,
    string? BuiltAt,
    IReadOnlyList<string> Paks,
    long SizeBytes,
    bool Stale,
    string? StaleReason);

public sealed record IndexPage(IReadOnlyList<IndexEntry> Items, int Total, string Profile, string? BuiltAt, bool Stale, string? StaleReason);

/// <summary>
/// Per-profile SQLite index of virtual paths. Paths are cheap to enumerate; class names are
/// not (they need a package load), so the index stores only name/extension/kind/size.
/// </summary>
public static class AssetIndex
{
    private static readonly object BuildLock = new();

    public static string DbPath(string indexRoot, string profile)
    {
        var safe = string.Concat(profile.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(indexRoot, safe + ".db");
    }

    public static IndexStatus Status(string dbPath, GameProfile profile)
    {
        if (!File.Exists(dbPath))
            return new IndexStatus(profile.Name, dbPath, false, 0, null, [], 0, false, null);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        var count = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM assets"));
        var meta = ReadMeta(connection);
        var paks = (meta.GetValueOrDefault("paks") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var (stale, reason) = PakFingerprint.Compare(profile, paks, meta.GetValueOrDefault("fingerprint"));
        return new IndexStatus(
            profile.Name,
            dbPath,
            true,
            count,
            meta.GetValueOrDefault("builtAt"),
            paks,
            new FileInfo(dbPath).Length,
            stale,
            reason);
    }

    public static IndexStatus Build(IFileProvider provider, string indexRoot, GameProfile profile, IReadOnlyList<string> paks)
    {
        lock (BuildLock)
        {
            var profileName = profile.Name;
            Directory.CreateDirectory(indexRoot);
            var dbPath = DbPath(indexRoot, profileName);
            var temp = dbPath + ".tmp";
            if (File.Exists(temp)) File.Delete(temp);

            using var connection = new SqliteConnection($"Data Source={temp}");
            connection.Open();
            Execute(connection, "PRAGMA journal_mode=OFF");
            Execute(connection, "PRAGMA synchronous=OFF");
            Execute(connection, "CREATE TABLE assets(path TEXT PRIMARY KEY, name TEXT, ext TEXT, kind TEXT, size INTEGER) WITHOUT ROWID");
            Execute(connection, "CREATE INDEX idx_assets_name ON assets(name)");
            Execute(connection, "CREATE INDEX idx_assets_kind ON assets(kind)");
            Execute(connection, "CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT)");

            var count = 0;
            using (var transaction = connection.BeginTransaction())
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR REPLACE INTO assets(path,name,ext,kind,size) VALUES($p,$n,$e,$k,$s)";
                var p = insert.Parameters.Add("$p", SqliteType.Text);
                var n = insert.Parameters.Add("$n", SqliteType.Text);
                var e = insert.Parameters.Add("$e", SqliteType.Text);
                var k = insert.Parameters.Add("$k", SqliteType.Text);
                var s = insert.Parameters.Add("$s", SqliteType.Integer);

                foreach (var (path, file) in provider.Files)
                {
                    var name = Path.GetFileName(path);
                    var ext = Path.GetExtension(path);
                    p.Value = path;
                    n.Value = name;
                    e.Value = ext;
                    k.Value = DshUeAsset.Core.Assets.AssetKind.Resolve(provider, null, path, name, ext);
                    s.Value = file.Size;
                    insert.ExecuteNonQuery();
                    count++;
                }

                using var meta = connection.CreateCommand();
                meta.Transaction = transaction;
                meta.CommandText = "INSERT OR REPLACE INTO meta(key,value) VALUES($k,$v)";
                void Put(string key, string value)
                {
                    meta.Parameters.Clear();
                    meta.Parameters.AddWithValue("$k", key);
                    meta.Parameters.AddWithValue("$v", value);
                    meta.ExecuteNonQuery();
                }
                Put("profile", profileName);
                Put("builtAt", DateTimeOffset.Now.ToString("O"));
                Put("paks", string.Join(',', paks));
                Put("fingerprint", PakFingerprint.Compute(profile, paks));
                Put("count", count.ToString());
                transaction.Commit();
            }

            connection.Close();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
            File.Move(temp, dbPath);
            return Status(dbPath, profile);
        }
    }

    public static IndexPage Search(string dbPath, GameProfile profile, string query, string? kind, bool nameOnly, int limit, int offset)
    {
        if (!File.Exists(dbPath))
            throw new AssetException(AssetErrorKind.NotFound, "index not built for this profile", new { profile = profile.Name });

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        var like = "%" + EscapeLike(query) + "%";
        var where = nameOnly ? "name LIKE $q ESCAPE '\\'" : "(name LIKE $q ESCAPE '\\' OR path LIKE $q ESCAPE '\\')";
        if (!string.IsNullOrWhiteSpace(kind)) where += " AND kind = $kind";

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM assets WHERE " + where;
        countCommand.Parameters.AddWithValue("$q", like);
        if (!string.IsNullOrWhiteSpace(kind)) countCommand.Parameters.AddWithValue("$kind", kind);
        var total = Convert.ToInt32(countCommand.ExecuteScalar());

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path,name,ext,kind,size FROM assets WHERE " + where + " ORDER BY path LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$q", like);
        if (!string.IsNullOrWhiteSpace(kind)) command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        command.Parameters.AddWithValue("$offset", Math.Max(offset, 0));

        var items = new List<IndexEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            items.Add(new IndexEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4)));

        var meta = ReadMeta(connection);
        var paks = (meta.GetValueOrDefault("paks") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var (stale, reason) = PakFingerprint.Compare(profile, paks, meta.GetValueOrDefault("fingerprint"));
        return new IndexPage(items, total, profile.Name, meta.GetValueOrDefault("builtAt"), stale, reason);
    }

    public static bool Drop(string dbPath)
    {
        if (!File.Exists(dbPath)) return false;
        SqliteConnection.ClearAllPools();
        File.Delete(dbPath);
        return true;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key,value FROM meta";
            using var reader = command.ExecuteReader();
            while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
        }
        catch (SqliteException)
        {
            // older index without the meta table
        }
        return result;
    }
}