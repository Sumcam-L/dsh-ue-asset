using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.VirtualFileSystem;

namespace DshUeAsset.Core.Sessions;

/// <summary>
/// AES state of one registered pak. CUE4Parse keeps the reader in UnloadedVfs until a matching
/// key arrives; EncryptionKeyGuid is read from the pak footer, so this works without mounting.
/// </summary>
/// NeedsOwnKey is false once the profile main key opens this pak, so the settings list only
/// shows archives that actually require an extra key of their own.
public sealed record PakKeyState(string Pak, bool Encrypted, string? KeyGuid, bool HasKey, bool NeedsOwnKey);

public static class PakKeys
{
    /// <summary>Every registered reader (mounted or not), deduplicated by file name.</summary>
    public static IReadOnlyList<IAesVfsReader> Readers(AbstractVfsFileProvider provider)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<IAesVfsReader>();
        foreach (var reader in provider.MountedVfs.Concat(provider.UnloadedVfs))
        {
            if (seen.Add(reader.Name)) list.Add(reader);
        }
        return list;
    }

    public static IAesVfsReader? Find(AbstractVfsFileProvider provider, string pakName) =>
        provider.UnloadedVfs.Concat(provider.MountedVfs)
            .FirstOrDefault(r => r.Name.Equals(pakName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// TestAesKey decrypts the index header and validates the mount point, which is exactly the
    /// check CUE4Parse performs while mounting — so a wrong key is rejected here instead of
    /// silently producing unreadable archives later (AbstractAesVfsReader.TestAesKey).
    /// </summary>
    public static bool AcceptsKey(IAesVfsReader reader, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        try { return reader.TestAesKey(new FAesKey(ProviderFactory.CleanKey(key))); }
        catch { return false; }
    }

    public static bool IsMounted(AbstractVfsFileProvider provider, IAesVfsReader reader) =>
        provider.MountedVfs.Contains(reader);

    /// <summary>
    /// Describe every pak, marking HasKey when the profile already stores a key for its GUID.
    /// A zero GUID is the "default" slot, which the profile AES key fills.
    /// Testing the main key decrypts this pak's index header — the same cost as one mount
    /// attempt — which is what lets NeedsOwnKey drop archives the main key already opens.
    /// </summary>
    public static IReadOnlyList<PakKeyState> Describe(AbstractVfsFileProvider provider, string? mainKey, Func<FGuid, bool>? hasKey = null)
    {
        var hasMain = !string.IsNullOrWhiteSpace(mainKey);
        var list = new List<PakKeyState>();
        foreach (var reader in Readers(provider))
        {
            var guid = reader.EncryptionKeyGuid;
            var mainWorks = !reader.IsEncrypted || (hasMain && AcceptsKey(reader, mainKey!));
            var known = !reader.IsEncrypted
                        || provider.Keys.ContainsKey(guid)
                        || (hasKey?.Invoke(guid) ?? false);
            list.Add(new PakKeyState(
                reader.Name,
                reader.IsEncrypted,
                reader.IsEncrypted ? guid.ToString() : null,
                known,
                reader.IsEncrypted && !mainWorks));
        }
        return list.OrderBy(state => state.Pak, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
