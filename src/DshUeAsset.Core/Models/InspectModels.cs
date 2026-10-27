namespace DshUeAsset.Core.Models;

public sealed record ExportSummary(int Index, string Name, string ClassName);

public sealed record InspectResult(
    ExportMeta Meta,
    IReadOnlyList<ExportSummary> Exports,
    string? PropertiesJson,
    bool Truncated,
    string? Note);
