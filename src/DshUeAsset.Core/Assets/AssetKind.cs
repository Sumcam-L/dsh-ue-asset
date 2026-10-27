using CUE4Parse.FileProvider;
using DshUeAsset.Core.Config;

namespace DshUeAsset.Core.Assets;

/// <summary>
/// Asset kind resolution. Order: AssetRegistry class (authoritative) → profile prefix rules →
/// built-in prefix conventions → generic. Prefixes are only a fallback: SKM_LODSettings is a
/// data asset, not a skeletal mesh, and the registry gets that right.
/// </summary>
public static class AssetKind
{
    public static readonly (string Prefix, string Kind)[] BuiltInPrefixes =
    [
        ("ABP_", "anim_blueprint"),
        ("SKEL_", "skeleton"),
        ("SK_", "skeletal_mesh"),
        ("SM_", "static_mesh"),
        ("T_", "texture"),
        ("MI_", "material_instance"),
        ("MF_", "material_function"),
        ("MPC_", "material_parameter_collection"),
        ("M_", "material"),
        ("BP_", "blueprint"),
        ("WBP_", "blueprint"),
        ("DT_", "data_table"),
        ("DA_", "data_asset"),
        ("AM_", "anim_montage"),
        ("AS_", "anim_sequence"),
        ("LS_", "level_sequence"),
        ("SQ_", "sequence"),
        ("NS_", "niagara"),
        ("PS_", "particle"),
        ("GE_", "gameplay_effect"),
        ("GA_", "gameplay_ability"),
        ("W_", "world"),
        ("S_", "sound")
    ];

    public static IReadOnlyList<PrefixRule> BuiltInRules =>
        BuiltInPrefixes.Select(p => new PrefixRule { Prefix = p.Prefix, Kind = p.Kind }).ToList();

    public static string Resolve(IFileProvider? provider, GameProfile? profile, string path, string? fileName = null, string? extension = null)
    {
        var name = fileName ?? Path.GetFileName(path);
        var ext = extension ?? Path.GetExtension(path);

        if (provider is not null)
        {
            var className = AssetRegistryCache.GetClass(provider, path);
            if (!string.IsNullOrWhiteSpace(className))
            {
                var mapped = ClassToKind(className!);
                if (mapped is not null) return mapped;
            }
        }

        var rules = profile?.PrefixRules;
        if (rules is { Count: > 0 })
        {
            var match = rules
                .Where(r => !string.IsNullOrWhiteSpace(r.Prefix))
                .OrderByDescending(r => r.Prefix.Length)
                .FirstOrDefault(r => name.StartsWith(r.Prefix, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !string.IsNullOrWhiteSpace(match.Kind)) return match.Kind!;
        }

        if (ext.Equals(".umap", StringComparison.OrdinalIgnoreCase)) return "world";
        foreach (var (prefix, kind) in BuiltInPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return kind;

        return "asset";
    }

    public static string? ClassToKind(string className)
    {
        var name = className;
        if (name.StartsWith("U", StringComparison.Ordinal) && name.Length > 1) name = name[1..];
        if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];

        if (name.Contains("BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("AnimBlueprint", StringComparison.OrdinalIgnoreCase) ? "anim_blueprint" : "blueprint";
        }

        return name.ToLowerInvariant() switch
        {
            "skeletalmesh" => "skeletal_mesh",
            "skeleton" => "skeleton",
            "staticmesh" => "static_mesh",
            "texture2d" or "texturecube" or "texturerendertarget" or "texture2darray" or "volume" => "texture",
            "material" => "material",
            "materialinstance" or "materialinstanceconstant" or "materialinstancedynamic" => "material_instance",
            "materialfunction" or "materialfunctioninstance" => "material_function",
            "materialparametercollection" => "material_parameter_collection",
            "animsequence" => "anim_sequence",
            "animmontage" => "anim_montage",
            "animcomposite" => "anim_composite",
            "levelsequence" => "level_sequence",
            "datatable" => "data_table",
            "dataasset" or "primarydataasset" or "lodsettings" or "skeletalmeshlodsettings" or "developersettings" or "settings" => "data_asset",
            "world" or "map" => "world",
            "soundwave" or "soundcue" or "soundclass" => "sound",
            "niagarasystem" or "niagaraemitter" => "niagara",
            "particlesystem" => "particle",
            "gameplayeffect" or "gameplayability" => "gameplay_effect",
            _ => null
        };
    }
}