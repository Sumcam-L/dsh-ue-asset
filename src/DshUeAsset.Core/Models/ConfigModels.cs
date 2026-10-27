using DshUeAsset.Core.Config;

namespace DshUeAsset.Core.Models;

public sealed record ProfileView(
    string Name,
    string Directory,
    IReadOnlyList<string> ExtraDirectories,
    string Game,
    string Platform,
    bool HasAesKey,
    int DynamicKeyCount,
    string? Usmap,
    IReadOnlyList<PrefixRule> PrefixRules);

public sealed record ConfigView(
    string ConfigPath,
    string CacheRoot,
    IReadOnlyList<ProfileView> Profiles,
    string OutputRoot,
    string? OodleLibrary,
    string DefaultMeshFormat,
    int MaxSessions,
    int MaxJobs,
    int MaxBatchAssets,
    string? LastProfile);

public sealed class ProfileInput
{
    public string? OriginalName { get; set; }
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
    public List<string>? ExtraDirectories { get; set; }
    public string? Game { get; set; }
    public string? Platform { get; set; }
    public string? AesKey { get; set; }
    public bool ClearAesKey { get; set; }
    public List<DynamicKeyInput>? DynamicKeys { get; set; }
    public string? Usmap { get; set; }
    public List<PrefixRule>? PrefixRules { get; set; }
}

public sealed class DynamicKeyInput
{
    public string Guid { get; set; } = "";
    public string Key { get; set; } = "";
}

public sealed class GlobalInput
{
    public string? OutputRoot { get; set; }
    public string? CacheRoot { get; set; }
    public string? OodleLibrary { get; set; }
    public string? DefaultMeshFormat { get; set; }
    public int? MaxSessions { get; set; }
    public int? MaxJobs { get; set; }
    public int? MaxBatchAssets { get; set; }
}