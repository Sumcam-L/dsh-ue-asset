using DshUeAsset.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DshUeAsset.Cli;

internal static class CliJson
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    public static string Serialize(object? value) => JsonConvert.SerializeObject(value, Settings);

    public static string Compact(object? value) => JsonConvert.SerializeObject(value, new JsonSerializerSettings
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    });
}

internal static class ErrorCodes
{
    public static string Of(AssetErrorKind kind) => kind switch
    {
        AssetErrorKind.InvalidArgument => "invalid_argument",
        AssetErrorKind.NotFound => "not_found",
        AssetErrorKind.NeedsKey => "needs_key",
        AssetErrorKind.NeedsPaks => "needs_paks",
        AssetErrorKind.Unsupported => "unsupported",
        AssetErrorKind.ParserError => "parser_error",
        AssetErrorKind.TooLarge => "too_large",
        AssetErrorKind.PathDenied => "path_denied",
        AssetErrorKind.RendererUnavailable => "renderer_unavailable",
        _ => "internal"
    };
}