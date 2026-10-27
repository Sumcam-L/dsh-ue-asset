namespace DshUeAsset.Core.Assets;

/// <summary>
/// Human names (mostly Chinese boss names) → filename tokens. This is intentionally small and
/// editable: it exists so "黑熊精" can find SK_GYCY_xiong_02 without guessing English spellings.
/// </summary>
public static class AssetAliases
{
    private static readonly Dictionary<string, string[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["黑熊精"] = ["Xiong", "HeiXiong"],
        ["黑熊"] = ["Xiong"],
        ["白衣秀士"] = ["BaiYi"],
        ["虎先锋"] = ["HuXianFeng", "Tiger", "HXF"],
        ["黄风大圣"] = ["HuangFeng", "YellowWind", "HFD"],
        ["亢金龙"] = ["KangJinLong", "KJL"],
        ["二郎神"] = ["ErLang", "Erlangshen"],
        ["孙悟空"] = ["WuKong", "Wukong"],
        ["悟空"] = ["WuKong", "Wukong"],
        ["猪八戒"] = ["ZhuBaJie", "Bajie", "ZBJ"],
        ["牛魔王"] = ["NiuMoWang", "NMW"],
        ["红孩儿"] = ["HongHaiEr", "HHE"],
        ["黄眉"] = ["HuangMei"],
        ["金角"] = ["JinJiao"],
        ["银角"] = ["YinJiao"],
        ["黑风大王"] = ["HeiFeng", "BlackWind"]
    };

    public static IReadOnlyList<string> Expand(string query)
    {
        var value = (query ?? "").Trim();
        if (value.Length == 0) return [];
        var result = new List<string> { value };
        foreach (var (name, tokens) in Table)
        {
            if (!value.Contains(name, StringComparison.OrdinalIgnoreCase) &&
                !name.Contains(value, StringComparison.OrdinalIgnoreCase)) continue;
            result.AddRange(tokens);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}