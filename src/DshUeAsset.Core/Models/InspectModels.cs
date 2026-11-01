namespace DshUeAsset.Core.Models;

public sealed record ExportSummary(int Index, string Name, string ClassName);

/// <summary>
/// One package import: an object this package references but does not contain. Path is the full
/// object path reconstructed from the import's outer chain ("/Game/..." or "/Script/..."), which is
/// what makes a cooked USharp data asset readable even though it carries no properties.
/// </summary>
public sealed record ImportSummary(int Index, string Name, string ClassName, string? Path);

public sealed record InspectResult(
    ExportMeta Meta,
    IReadOnlyList<ExportSummary> Exports,
    IReadOnlyList<ImportSummary> Imports,
    string? PropertiesJson,
    bool Truncated,
    string? Note);
