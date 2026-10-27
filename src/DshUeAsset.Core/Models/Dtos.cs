namespace DshUeAsset.Core.Models;

public sealed record MountReportDto(
    int Mounted,
    int Submitted,
    IReadOnlyList<string> MissingKeys,
    long FileCount,
    string? EngineVersion,
    string? Warning);

public sealed record SessionSummary(
    string SessionId,
    string Profile,
    string Directory,
    string Game,
    string Platform,
    int Mounted,
    int Missing,
    bool NeedsKey,
    long FileCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    int RunningJobs,
    IReadOnlyList<string> Paks);

public sealed record TreeEntry(
    string Name,
    string Path,
    bool IsDirectory,
    long Size,
    string? Extension,
    int? Count,
    string Kind);

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Offset,
    int? NextOffset);

public sealed record ExportMeta(
    string Path,
    bool IsGameFile,
    long Size,
    string? Extension,
    string? ClassName,
    int ExportCount,
    int ImportCount);

public sealed record PreviewResult(
    string ObjectPath,
    string ClassName,
    string Kind,
    string? Mime,
    string? Base64,
    int? Width,
    int? Height,
    object? Meta,
    string? Note);

public sealed record ExportFileView(
    string ObjectPath,
    bool Success,
    IReadOnlyList<string> Files,
    string? Error);

public sealed record JobView(
    string JobId,
    string Kind,
    string Status,
    int Total,
    int Completed,
    int Percent,
    string? Message,
    string? Error,
    IReadOnlyList<ExportFileView> Results,
    string? OutputDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Skipped,
    string? Source);
public sealed record SearchResult(string Query, IReadOnlyList<string> Aliases, IReadOnlyList<TreeEntry> Items, int Total);
