namespace DshUeAsset.Core;

/// <summary>Stable error kinds shared by the CLI, the JSON-lines server and the DSH tools.</summary>
public enum AssetErrorKind
{
    InvalidArgument,
    NotFound,
    NeedsKey,
    NeedsPaks,
    Unsupported,
    ParserError,
    TooLarge,
    PathDenied,
    RendererUnavailable,
    Internal
}

public sealed class AssetException : Exception
{
    public AssetException(AssetErrorKind kind, string message, object? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Details = details;
    }

    public AssetErrorKind Kind { get; }
    public object? Details { get; }

    public static AssetException Invalid(string message, object? details = null) => new(AssetErrorKind.InvalidArgument, message, details);
    public static AssetException NotFound(string message, object? details = null) => new(AssetErrorKind.NotFound, message, details);
    public static AssetException Unsupported(string message, object? details = null) => new(AssetErrorKind.Unsupported, message, details);
    public static AssetException TooLarge(string message, object? details = null) => new(AssetErrorKind.TooLarge, message, details);
    public static AssetException PathDenied(string message, object? details = null) => new(AssetErrorKind.PathDenied, message, details);
}