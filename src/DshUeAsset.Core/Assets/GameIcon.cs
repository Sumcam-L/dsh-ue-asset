using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DshUeAsset.Core.Config;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DshUeAsset.Core.Assets;

/// <summary>
/// Pulls the executable icon out of a game's PE resources so the settings cards can show the game's
/// own icon instead of a generic glyph. Every failure degrades to <c>null</c>: an icon is cosmetic and
/// must never block browsing.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GameIcon
{
    private const int IconSize = 64;

    private sealed record Cached(DateTime MtimeUtc, long Length, byte[]? Png);

    private static readonly ConcurrentDictionary<string, Cached> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves the icon for a profile; <paramref name="exe"/> is the executable it came from.</summary>
    public static (string? Exe, byte[]? Png) Resolve(GameProfile profile)
    {
        var exe = FindExe(profile);
        if (exe is null) return (null, null);

        try
        {
            var info = new FileInfo(exe);
            if (!info.Exists) return (null, null);
            if (Cache.TryGetValue(exe, out var cached) && cached.MtimeUtc == info.LastWriteTimeUtc && cached.Length == info.Length)
                return (exe, cached.Png);

            var png = ExtractPng(exe);
            Cache[exe] = new Cached(info.LastWriteTimeUtc, info.Length, png);
            return (exe, png);
        }
        catch
        {
            return (exe, null);
        }
    }

    private static string? FindExe(GameProfile profile)
    {
        var starts = new List<string> { profile.Directory };
        starts.AddRange(profile.ExtraDirectories);

        foreach (var start in starts)
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var dir = start;
            // Paks -> Content -> <game> -> <install root>: three hops covers the common layouts.
            for (var depth = 0; depth < 4 && !string.IsNullOrWhiteSpace(dir); depth++)
            {
                var binaries = Path.Combine(dir, "Binaries", "Win64");
                var shipping = FirstExe(binaries, "*-Win64-Shipping.exe");
                if (shipping is not null) return shipping;

                var fromBinaries = FirstExe(binaries, "*.exe");
                if (fromBinaries is not null) return fromBinaries;

                var launcher = FirstExe(dir, "*.exe");
                if (launcher is not null) return launcher;

                dir = Path.GetDirectoryName(dir);
            }
        }
        return null;
    }

    private static string? FirstExe(string directory, string pattern)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;
            return Directory.GetFiles(directory, pattern)
                .Where(path => !IsHelper(Path.GetFileName(path)))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHelper(string name) =>
        name.Contains("unins", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("injector", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("report", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("副本", StringComparison.Ordinal);

    private static byte[]? ExtractPng(string exe)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];
        var count = PrivateExtractIcons(exe, 0, IconSize, IconSize, icons, ids, 1, 0);
        if (count == 0 || icons[0] == IntPtr.Zero) return null;

        try
        {
            return RenderPng(icons[0]);
        }
        finally
        {
            DestroyIcon(icons[0]);
        }
    }

    private static byte[]? RenderPng(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var info)) return null;
        try
        {
            if (info.hbmColor == IntPtr.Zero) return null;
            if (GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), out var bitmap) == 0) return null;

            var width = bitmap.bmWidth;
            var height = bitmap.bmHeight;
            if (width <= 0 || height <= 0 || width > 256 || height > 256) return null;

            var dc = GetDC(IntPtr.Zero);
            try
            {
                var pixels = ReadPixels(dc, info.hbmColor, width, height);
                if (pixels is null) return null;

                // Older icons carry a zero alpha channel and rely on the AND mask instead.
                if (IsFullyTransparent(pixels))
                {
                    var mask = ReadMask(dc, info.hbmMask, width, height);
                    if (mask is not null) ApplyMask(pixels, mask, width, height);
                }

                using var image = Image.LoadPixelData<Bgra32>(pixels, width, height);
                using var output = new MemoryStream();
                image.SaveAsPng(output);
                return output.ToArray();
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
    }

    private static byte[]? ReadPixels(IntPtr dc, IntPtr bitmap, int width, int height)
    {
        var header = Header(width, height, 32);
        var pixels = new byte[width * height * 4];
        return GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == 0 ? null : pixels;
    }

    private static byte[]? ReadMask(IntPtr dc, IntPtr mask, int width, int height)
    {
        var header = Header(width, height, 1);
        var stride = (width + 31) / 32 * 4;
        var bits = new byte[stride * height];
        return GetDIBits(dc, mask, 0, (uint)height, bits, ref header, 0) == 0 ? null : bits;
    }

    private static BITMAPINFO Header(int width, int height, ushort bitCount) => new()
    {
        bmiHeader = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down, matching ImageSharp's row order
            biPlanes = 1,
            biBitCount = bitCount,
            biCompression = 0, // BI_RGB
            biSizeImage = 0
        },
        bmiColors = new byte[256 * 4]
    };

    private static bool IsFullyTransparent(byte[] bgra)
    {
        for (var index = 3; index < bgra.Length; index += 4)
        {
            if (bgra[index] != 0) return false;
        }
        return true;
    }

    private static void ApplyMask(byte[] bgra, byte[] mask, int width, int height)
    {
        var stride = (width + 31) / 32 * 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var bit = (mask[y * stride + x / 8] >> (7 - (x % 8))) & 1;
                bgra[(y * width + x) * 4 + 3] = bit == 1 ? (byte)0 : (byte)255;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256 * 4)]
        public byte[] bmiColors;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        [Out] IntPtr[] phicon, [Out] uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines,
        [Out] byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);
}
