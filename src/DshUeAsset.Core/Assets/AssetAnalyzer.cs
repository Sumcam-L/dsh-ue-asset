using CUE4Parse.FileProvider;
using Newtonsoft.Json.Linq;

namespace DshUeAsset.Core.Assets;

/// <summary>
/// Type-aware definition cards. Raw `propertiesJson` stays available, but the AI gets a stable
/// shape per class instead of having to know UE's serialized field names.
/// </summary>
public static class AssetAnalyzer
{
    public static JObject Analyze(IFileProvider provider, string objectPath, bool includeRaw, int maxChars)
    {
        var inspect = ObjectReader.Inspect(provider, objectPath, "all", Math.Max(maxChars, 100_000), 200);
        var root = Parse(inspect.PropertiesJson);
        var props = root["Properties"] as JObject ?? new JObject();
        var className = inspect.Meta.ClassName ?? "";
        var name = Path.GetFileName(objectPath);
        var kind = AssetKind.Resolve(provider, null, objectPath, name, inspect.Meta.Extension);

        var hasBlueprintExport = inspect.Exports.Any(e =>
            e.ClassName.Contains("BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase));
        var card = hasBlueprintExport
            ? BlueprintCard(props, inspect)
            : className switch
        {
            var c when c.Contains("MaterialInstance", StringComparison.OrdinalIgnoreCase) => MaterialCard(provider, objectPath, props, className, true),
            var c when c.Contains("Material", StringComparison.OrdinalIgnoreCase) && !c.Contains("Expression") => MaterialCard(provider, objectPath, props, className, false),
            var c when c.Contains("BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) => BlueprintCard(props, inspect),
            var c when c.Contains("DataTable", StringComparison.OrdinalIgnoreCase) => DataTableCard(props),
            var c when c.Contains("Texture", StringComparison.OrdinalIgnoreCase) => TextureCard(provider, objectPath, props),
            var c when c.Contains("AnimSequence", StringComparison.OrdinalIgnoreCase) || c.Contains("AnimMontage", StringComparison.OrdinalIgnoreCase) => AnimCard(props),
            _ => GenericCard(props, inspect)
        };

        var result = new JObject
        {
            ["path"] = objectPath,
            ["className"] = className,
            ["kind"] = kind,
            ["common"] = new JObject
            {
                ["size"] = inspect.Meta.Size,
                ["exportCount"] = inspect.Meta.ExportCount,
                ["importCount"] = inspect.Meta.ImportCount,
                ["truncated"] = inspect.Truncated,
                ["note"] = inspect.Note
            },
            ["card"] = card
        };

        if (includeRaw)
        {
            result["raw"] = new JObject
            {
                ["truncated"] = inspect.Truncated,
                ["propertiesJson"] = inspect.PropertiesJson
            };
        }

        return result;
    }

    private static JObject MaterialCard(IFileProvider provider, string objectPath, JObject props, string className, bool isInstance)
    {
        var scalars = new JObject();
        var vectors = new JObject();
        var textures = new JObject();
        var parents = new JArray();

        var parent = ObjectPathOf(props["Parent"]);
        if (!string.IsNullOrWhiteSpace(parent)) parents.Add(parent);

        Collect(props, scalars, vectors, textures);

        // Walk the parent chain; child values win and missing ones are filled from parents.
        var current = parent;
        for (var depth = 0; depth < 4 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            var next = TrimIndex(current!);
            if (next.Equals(objectPath, StringComparison.OrdinalIgnoreCase)) break;
            try
            {
                var parentInspect = ObjectReader.Inspect(provider, next, "properties", 400_000, 50);
                var parentRoot = Parse(parentInspect.PropertiesJson);
                var parentProps = parentRoot["Properties"] as JObject ?? new JObject();
                Collect(parentProps, scalars, vectors, textures, overwrite: false);
                var grand = ObjectPathOf(parentProps["Parent"]);
                if (!string.IsNullOrWhiteSpace(grand))
                {
                    parents.Add(grand);
                    current = grand;
                }
                else break;
            }
            catch
            {
                break;
            }
        }

        var overrides = new JObject();
        if (props["BasePropertyOverrides"] is JObject bo)
            foreach (var p in bo.Properties()) overrides[p.Name] = p.Value.DeepClone();

        var card = new JObject
        {
            ["type"] = "material",
            ["instance"] = isInstance,
            ["parent"] = parent,
            ["parents"] = parents,
            ["scalars"] = scalars,
            ["vectors"] = vectors,
            ["textures"] = textures,
            ["overrides"] = overrides,
            ["blendMode"] = props["BlendMode"]?.ToString(),
            ["shadingModel"] = props["ShadingModel"]?.ToString(),
            ["twoSided"] = props["TwoSided"]?.ToObject<bool?>(),
            ["masked"] = props["bIsMasked"]?.ToObject<bool?>()
        };
        return card;
    }

    private static void Collect(JObject props, JObject scalars, JObject vectors, JObject textures, bool overwrite = true)
    {
        if (props["ScalarParameterValues"] is JArray scalarValues)
        {
            foreach (var entry in scalarValues.OfType<JObject>())
            {
                var name = entry["ParameterInfo"]?["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!overwrite && scalars[name] is not null) continue;
                scalars[name] = entry["ParameterValue"]?.DeepClone();
            }
        }

        if (props["VectorParameterValues"] is JArray vectorValues)
        {
            foreach (var entry in vectorValues.OfType<JObject>())
            {
                var name = entry["ParameterInfo"]?["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!overwrite && vectors[name] is not null) continue;
                vectors[name] = entry["ParameterValue"]?.DeepClone();
            }
        }

        if (props["TextureParameterValues"] is JArray textureValues)
        {
            foreach (var entry in textureValues.OfType<JObject>())
            {
                var name = entry["ParameterInfo"]?["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!overwrite && textures[name] is not null) continue;
                textures[name] = ObjectPathOf(entry["ParameterValue"]);
            }
        }
    }

    private static JObject BlueprintCard(JObject props, Models.InspectResult inspect)
    {
        var functions = new JArray();
        foreach (var export in inspect.Exports.Where(e => e.ClassName == "Function"))
            functions.Add(new JObject { ["name"] = export.Name, ["index"] = export.Index });

        var animNodes = new JArray();
        foreach (var property in props.Properties())
            if (property.Name.StartsWith("AnimGraphNode_", StringComparison.OrdinalIgnoreCase))
                animNodes.Add(property.Name);

        var components = new JArray();
        if (props["SimpleConstructionScript"]?["Nodes"] is JArray nodes)
            foreach (var node in nodes.OfType<JObject>())
                components.Add(node["ComponentName"]?.ToString() ?? node["Name"]?.ToString());
        else if (props["BlueprintCreatedComponents"] is JArray created)
            foreach (var item in created.OfType<JObject>())
                components.Add(item["Name"]?.ToString() ?? item["ObjectName"]?.ToString());

        var interfaces = new JArray();
        if (props["Interfaces"] is JArray ifaces)
            foreach (var item in ifaces.OfType<JObject>())
                interfaces.Add(ObjectPathOf(item));

        return new JObject
        {
            ["type"] = "blueprint",
            ["isAnimBlueprint"] = animNodes.Count > 0,
            ["uberGraph"] = props["UberGraphFrame"] is not null,
            ["functions"] = functions,
            ["animGraphNodes"] = animNodes,
            ["components"] = components,
            ["interfaces"] = interfaces,
            ["cdoExport"] = inspect.Exports.FirstOrDefault(e => e.Name.StartsWith("Default__", StringComparison.Ordinal))?.Name,
            ["note"] = "cooked packages usually drop the Ubergraph/function node graph; bytecode is not decompiled"
        };
    }

    private static JObject DataTableCard(JObject props)
    {
        var rows = props["RowMap"] as JObject;
        var sample = new JArray();
        if (rows is not null)
            foreach (var row in rows.Properties().Take(5))
                sample.Add(new JObject
                {
                    ["row"] = row.Name,
                    ["fields"] = row.Value is JObject o ? new JArray(o.Properties().Select(p => p.Name)) : new JArray()
                });

        return new JObject
        {
            ["type"] = "data_table",
            ["rowStruct"] = ObjectPathOf(props["RowStruct"]),
            ["rowCount"] = rows?.Count,
            ["sample"] = sample,
            ["truncatedRows"] = rows is null
        };
    }

    private static JObject TextureCard(IFileProvider provider, string objectPath, JObject props)
    {
        var card = new JObject
        {
            ["type"] = "texture",
            ["width"] = props["SizeX"]?.ToObject<int?>(),
            ["height"] = props["SizeY"]?.ToObject<int?>(),
            ["format"] = props["PixelFormat"]?.ToString() ?? props["Format"]?.ToString()
        };
        try
        {
            if (ObjectReader.Load(provider, objectPath) is CUE4Parse.UE4.Assets.Exports.Texture.UTexture texture)
            {
                var preview = TexturePreview.Render(texture, objectPath, 64);
                card["width"] = preview.Width;
                card["height"] = preview.Height;
                card["format"] = preview.Meta?.GetType().GetProperty("format")?.GetValue(preview.Meta)?.ToString();
                card["pixelFormat"] = preview.Meta?.GetType().GetProperty("pixelFormat")?.GetValue(preview.Meta)?.ToString();
            }
        }
        catch
        {
            // keep whatever the serialized properties exposed
        }
        return card;
    }

    private static JObject AnimCard(JObject props) => new()
    {
        ["type"] = "animation",
        ["length"] = props["SequenceLength"]?.ToObject<double?>(),
        ["rateScale"] = props["RateScale"]?.ToObject<double?>(),
        ["skeleton"] = ObjectPathOf(props["Skeleton"]),
        ["notifyCount"] = (props["Notifies"] as JArray)?.Count
    };

    private static JObject GenericCard(JObject props, Models.InspectResult inspect)
    {
        var keys = new JArray(props.Properties().Take(200).Select(p => p.Name));
        return new JObject
        {
            ["type"] = "generic",
            ["propertyCount"] = props.Count,
            ["propertyKeys"] = keys,
            ["mappingMissing"] = props.Count == 0 && inspect.Meta.ImportCount > 0,
            ["note"] = props.Count == 0 ? "no serialized properties: class mapping may be missing for this profile" : null
        };
    }

    private static JObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try { return JObject.Parse(json); }
        catch { return new JObject(); }
    }

    private static string? ObjectPathOf(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null) return null;
        if (token is JObject obj)
        {
            var path = obj["ObjectPath"]?.ToString();
            if (!string.IsNullOrWhiteSpace(path)) return path;
            var name = obj["ObjectName"]?.ToString();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        return token.ToString();
    }

    private static string TrimIndex(string objectPath)
    {
        var dot = objectPath.LastIndexOf('.');
        return dot > 0 && int.TryParse(objectPath[(dot + 1)..], out _) ? objectPath[..dot] : objectPath;
    }
}