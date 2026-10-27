namespace DshUeAsset.Core.Models;

public sealed record PakInfo(
    string Name,
    string Path,
    long Size,
    int Chunk,
    bool IsUtoc,
    IReadOnlyList<string> Tags);

public sealed record PakCatalogView(
    string Profile,
    IReadOnlyList<PakInfo> Available,
    IReadOnlyList<string> Defaults);
