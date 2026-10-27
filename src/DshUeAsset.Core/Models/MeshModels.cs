namespace DshUeAsset.Core.Models;

public sealed record MeshMaterialBinding(string Material, string? BaseColor);

public sealed record MeshPreviewResult(
    string CacheDirectory,
    string EntryFile,
    string RelativeEntry,
    string ClassName,
    long Bytes,
    IReadOnlyList<string> Files,
    bool Animations,
    bool Materials,
    IReadOnlyList<MeshMaterialBinding> MaterialBindings);
