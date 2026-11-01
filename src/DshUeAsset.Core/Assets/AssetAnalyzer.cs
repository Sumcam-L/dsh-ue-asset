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
        // 0 disables truncation: the complete tree is required, and a cut-off dump parses to {}.
        var inspect = ObjectReader.Inspect(provider, objectPath, "all", 0, 200);
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
            var c when c.Contains("DataTable", StringComparison.OrdinalIgnoreCase) => DataTableCard(root),
            var c when c.Contains("Texture", StringComparison.OrdinalIgnoreCase) => TextureCard(provider, objectPath, root, props),
            var c when c.Contains("AnimSequence", StringComparison.OrdinalIgnoreCase) || c.Contains("AnimMontage", StringComparison.OrdinalIgnoreCase) => AnimCard(root, props),
            var c when c.Contains("SkeletalMesh", StringComparison.OrdinalIgnoreCase) => MeshCard(root, props, skeletal: true),
            var c when c.Contains("StaticMesh", StringComparison.OrdinalIgnoreCase) => MeshCard(root, props, skeletal: false),
            var c when c.Contains("Skeleton", StringComparison.OrdinalIgnoreCase) => SkeletonCard(root, props),
            var c when c.Contains("SoundWave", StringComparison.OrdinalIgnoreCase) || c.Contains("SoundCue", StringComparison.OrdinalIgnoreCase) => SoundCard(root, props),
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

        // A material instance only serializes its parent and overrides; blend mode, shading model
        // and usage flags live on the base material, so inherit them from the first parent.
        var info = props;

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
                // Keep walking until a layer actually carries the flags (instances usually do not).
                if (info["ShadingModel"] is null && parentProps["ShadingModel"] is not null) info = parentProps;
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

        // Cooked base materials keep almost nothing: no graph, no parameters, no blend mode.
        // The bUsedWith* flags and the shading model are what survives, so surface them.
        var usedWith = new JArray();
        foreach (var property in info.Properties())
            if (property.Name.StartsWith("bUsedWith", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ToObject<bool?>() == true)
                usedWith.Add(property.Name.Substring("bUsedWith".Length));

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
            ["blendMode"] = info["BlendMode"]?.ToString(),
            ["shadingModel"] = info["ShadingModel"]?.ToString(),
            ["twoSided"] = info["TwoSided"]?.ToObject<bool?>(),
            ["masked"] = info["bIsMasked"]?.ToObject<bool?>(),
            ["usedWith"] = usedWith,
            ["materialAttributes"] = info["MaterialAttributes"]?["ExpressionName"]?.ToString(),
            // True when blend/shading/usage came from a parent instead of the asset itself.
            ["inherited"] = !ReferenceEquals(info, props)
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

    // CUE4Parse writes the parsed rows to a root-level "Rows" member (UDataTable.WriteJson),
    // not to Properties, so the row data is only reachable from the whole document.
    private const int TableRowLimit = 50;

    private static JObject DataTableCard(JObject root)
    {
        var rows = root["Rows"] as JObject;
        var columns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new JArray();
        if (rows is not null)
            foreach (var row in rows.Properties().Take(TableRowLimit))
            {
                var values = new JObject();
                if (row.Value is JObject fields)
                    foreach (var field in fields.Properties())
                    {
                        columns.Add(field.Name);
                        values[field.Name] = Shorten(field.Value);
                    }
                list.Add(new JObject { ["row"] = row.Name, ["values"] = values });
            }

        return new JObject
        {
            ["type"] = "data_table",
            ["rowStruct"] = ObjectPathOf(root["Properties"]?["RowStruct"]),
            ["rowCount"] = rows?.Count ?? 0,
            ["columns"] = new JArray(columns),
            ["rows"] = list,
            ["truncatedRows"] = rows is not null && rows.Count > TableRowLimit
        };
    }

    /// <summary>Table cells keep their JSON shape unless it is too long to render.</summary>
    private static JToken Shorten(JToken value)
    {
        switch (value.Type)
        {
            case JTokenType.Object:
            case JTokenType.Array:
                var json = value.ToString(Newtonsoft.Json.Formatting.None);
                return json.Length <= 160 ? value.DeepClone() : json[..160] + "…";
            case JTokenType.String:
                var text = value.ToString();
                return text.Length <= 160 ? value.DeepClone() : text[..160] + "…";
            default:
                return value.DeepClone();
        }
    }

    private static JObject TextureCard(IFileProvider provider, string objectPath, JObject root, JObject props)
    {
        // On a cooked Texture2D the dimensions and pixel format sit on the object itself;
        // Properties only carries the import settings.
        var card = new JObject
        {
            ["type"] = "texture",
            ["width"] = root["SizeX"]?.ToObject<int?>() ?? props["SizeX"]?.ToObject<int?>(),
            ["height"] = root["SizeY"]?.ToObject<int?>() ?? props["SizeY"]?.ToObject<int?>(),
            ["format"] = root["PixelFormat"]?.ToString() ?? props["PixelFormat"]?.ToString() ?? props["Format"]?.ToString(),
            ["compression"] = props["CompressionSettings"]?.ToString(),
            ["srgb"] = props["sRGB"]?.ToObject<bool?>(),
            ["virtual"] = props["VirtualTextureStreaming"]?.ToObject<bool?>(),
            ["mipZero"] = root["FirstMipToSerialize"]?.ToObject<int?>()
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

    private static JObject AnimCard(JObject root, JObject props)
    {
        double? fps = null;
        if (props["TargetFrameRate"] is JObject rate && rate["Numerator"]?.ToObject<double?>() is { } numerator && numerator > 0)
        {
            var denominator = rate["Denominator"]?.ToObject<double?>() ?? 1;
            if (denominator > 0) fps = numerator / denominator;
        }
        if (fps is null && props["RateScale"]?.ToObject<double?>() is { } scale && scale > 0) fps = scale;

        var structure = root["CompressedDataStructure"];
        return new JObject
        {
            ["type"] = "animation",
            ["length"] = props["SequenceLength"]?.ToObject<double?>(),
            ["fps"] = fps is > 0 ? Math.Round(fps.Value, 2) : null,
            ["frames"] = structure?["CompressedNumberOfFrames"]?.ToObject<int?>(),
            ["rateScale"] = props["RateScale"]?.ToObject<double?>(),
            ["skeleton"] = ObjectPathOf(props["Skeleton"]),
            ["notifyCount"] = (props["Notifies"] as JArray)?.Count,
            ["tracks"] = (root["CompressedTrackToSkeletonMapTable"] as JArray)?.Count,
            ["compression"] = structure?["KeyEncodingFormat"]?.ToString(),
            ["compressedBytes"] = root["CompressedRawDataSize"]?.ToObject<long?>()
        };
    }

    private static JObject MeshCard(JObject root, JObject props, bool skeletal)
    {
        var slots = props["StaticMaterials"] as JArray ?? props["SkeletalMaterials"] as JArray;
        var materials = new JArray();
        if (slots is not null)
            foreach (var item in slots.OfType<JObject>())
                materials.Add(new JObject
                {
                    ["slot"] = item["MaterialSlotName"]?.ToString() ?? item["ImportedMaterialSlotName"]?.ToString(),
                    ["path"] = ObjectPathOf(item["MaterialInterface"])
                });

        // RenderData survives cooking and carries per-LOD sections, so triangle and vertex
        // counts are derivable even though the mesh itself is not loaded.
        var lods = root["RenderData"]?["LODs"] as JArray;
        long triangles = 0;
        long vertices = 0;
        if (lods is not null)
            foreach (var lod in lods.OfType<JObject>())
            {
                if (lod["Sections"] is not JArray sections) continue;
                var maxIndex = -1;
                foreach (var section in sections.OfType<JObject>())
                {
                    triangles += section["NumTriangles"]?.ToObject<long?>() ?? 0;
                    var top = section["MaxVertexIndex"]?.ToObject<int?>() ?? -1;
                    if (top > maxIndex) maxIndex = top;
                }
                vertices += maxIndex + 1;
            }

        var card = new JObject
        {
            ["type"] = "mesh",
            ["meshKind"] = skeletal ? "skeletal" : "static",
            ["materialSlots"] = materials,
            ["lodCount"] = lods?.Count,
            ["triangles"] = lods is null ? null : triangles,
            ["vertices"] = lods is null ? null : vertices,
            ["collision"] = root["BodySetup"] is not null
        };

        if (skeletal)
        {
            card["bones"] = (props["BoneTree"] as JArray)?.Count
                            ?? (root["ReferenceSkeleton"]?["FinalRefBoneInfo"] as JArray)?.Count;
            card["sockets"] = Names(props["Sockets"]);
            card["slotGroups"] = new JArray((props["SlotGroups"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(group => group["GroupName"]?.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name)));
        }

        if (props["ExtendedBounds"] is JObject bounds && bounds["BoxExtent"] is JObject extent)
            card["extent"] = new JObject
            {
                ["x"] = extent["X"]?.ToObject<double?>(),
                ["y"] = extent["Y"]?.ToObject<double?>(),
                ["z"] = extent["Z"]?.ToObject<double?>()
            };

        return card;
    }

    private static JObject SkeletonCard(JObject root, JObject props)
    {
        var bones = new JArray();
        if (root["ReferenceSkeleton"]?["FinalRefBoneInfo"] is JArray info)
            bones = new JArray(info.OfType<JObject>()
                .Select(bone => bone["Name"]?.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name)));
        else if (props["BoneTree"] is JArray tree)
            bones = new JArray(tree.Count == 0 ? Array.Empty<string>() : Enumerable.Repeat("(unnamed)", tree.Count));

        return new JObject
        {
            ["type"] = "skeleton",
            ["boneCount"] = bones.Count,
            ["bones"] = bones,
            ["sockets"] = Names(props["Sockets"]),
            ["slotGroups"] = new JArray((props["SlotGroups"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(group => group["GroupName"]?.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name)))
        };
    }

    private static JObject SoundCard(JObject root, JObject props)
    {
        var platform = root["RunningPlatformData"];
        var chunks = platform?["Chunks"] as JArray;
        long bytes = 0;
        if (chunks is not null)
            foreach (var chunk in chunks.OfType<JObject>())
                bytes += chunk["DataSize"]?.ToObject<long?>() ?? 0;

        var channels = props["NumChannels"]?.ToObject<int?>();
        var sampleRate = props["SampleRate"]?.ToObject<int?>();
        var duration = props["Duration"]?.ToObject<double?>();

        return new JObject
        {
            ["type"] = "sound",
            ["duration"] = duration,
            ["channels"] = channels,
            ["sampleRate"] = sampleRate,
            ["volume"] = props["Volume"]?.ToObject<double?>(),
            ["looping"] = props["bLooping"]?.ToObject<bool?>(),
            ["quality"] = props["CompressionQuality"]?.ToObject<int?>(),
            ["format"] = platform?["AudioFormat"]?.ToString(),
            ["chunks"] = chunks?.Count,
            ["bytes"] = chunks is null ? null : bytes,
            ["kbps"] = channels is > 0 && sampleRate is > 0 && duration is > 0 && chunks is not null
                ? Math.Round(bytes * 8.0 / duration.Value / 1000.0, 1)
                : null
        };
    }

    private static JArray Names(JToken? token) => new((token as JArray ?? new JArray())
        .OfType<JObject>()
        .Select(item => item["ObjectName"]?.ToString() ?? item["ObjectPath"]?.ToString())
        .Where(name => !string.IsNullOrWhiteSpace(name)));

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