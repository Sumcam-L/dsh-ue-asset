using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;
using DshUeAsset.Core.Models;

namespace DshUeAsset.Core.Assets;

public static class TexturePreview
{
    public const int MaxDimension = 4096;

    public static PreviewResult Render(UTexture texture, string objectPath, int maxSize)
    {
        maxSize = Math.Clamp(maxSize <= 0 ? 1024 : maxSize, 64, MaxDimension);
        try
        {
            // A max-size hint larger than the asset itself can make the mip lookup return null;
            // fall back to the first mip instead of failing the preview.
            var decoded = TextureDecoder.Decode(texture, maxSize, ETexturePlatform.DesktopMobile)
                          ?? TextureDecoder.Decode(texture, ETexturePlatform.DesktopMobile)
                          ?? throw new AssetException(AssetErrorKind.Unsupported, "texture has no decodable mip", new { path = objectPath });
            var png = TextureEncoder.Encode(decoded, ETextureFormat.Png, saveHdrAsHdr: false, out var extension);
            var meta = new
            {
                width = decoded.Width,
                height = decoded.Height,
                pixelFormat = decoded.PixelFormat.ToString(),
                format = texture.Format.ToString(),
                encoded = extension
            };
            return new PreviewResult(objectPath, SafeClass(texture), "texture", "image/png", Convert.ToBase64String(png), decoded.Width, decoded.Height, meta, null);
        }
        catch (Exception ex)
        {
            throw new AssetException(AssetErrorKind.ParserError, $"texture decode failed: {ex.Message}", new { path = objectPath }, ex);
        }
    }

    public static bool IsTexture(object value) => value is UTexture;

    private static string SafeClass(object value)
    {
        try { return value.GetType().Name; } catch { return "UTexture"; }
    }
}