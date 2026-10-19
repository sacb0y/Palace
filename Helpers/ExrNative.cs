using System.Runtime.InteropServices;

namespace Palace.Helpers;

/// <summary>
/// Bundled OpenEXR (OpenEXRCore 3.5) via <c>palace_exr.dll</c>
/// (tools/openexr/palace_exr.c, built by tools/openexr/build-openexr.sh).
/// Decodes every OpenEXR compression (NONE, RLE, ZIPS, ZIP, PIZ, PXR24,
/// B44/B44A, DWAA/DWAB, HTJ2K, ZSTD), scanline or tiled (level 0), single
/// or multi-part (first RGB(A) / Y image part). Deep parts and sub-sampled
/// RY/BY channels are not decoded. Absent library (e.g. Linux test runs)
/// means <see cref="IsAvailable"/> is false and callers use the in-house
/// <see cref="ExrFile"/> reader.
/// </summary>
public static class ExrNative
{
    private const string Lib = "palace_exr";

    public const int FlagTiled = 1;
    public const int FlagMultipart = 2;
    public const int FlagLuminance = 4;
    public const int FlagHasAlpha = 8;

    public readonly record struct Info(int Width, int Height, int Flags, int Compression)
    {
        public bool Tiled => (Flags & FlagTiled) != 0;

        public bool Multipart => (Flags & FlagMultipart) != 0;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pexr_version")]
    private static extern IntPtr NativeVersion();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pexr_info")]
    private static extern int NativeInfo(
        in byte data,
        long size,
        out int width,
        out int height,
        out int flags,
        out int compression);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pexr_decode_rgba")]
    private static extern int NativeDecode(in byte data, long size, ref float dst, long dstFloats);

    private static readonly Lazy<string?> LoadedVersion = new(() =>
    {
        try
        {
            var p = NativeVersion();
            return p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    });

    public static bool IsAvailable => LoadedVersion.Value is not null;

    /// <summary>e.g. "OpenEXR 3.5.2", or null when the library is missing.</summary>
    public static string? Version => LoadedVersion.Value;

    /// <summary>Header-only: the image part <see cref="TryDecode"/> would decode.</summary>
    public static Info? TryInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !ExrFile.IsExr(data) || !IsAvailable)
        {
            return null;
        }

        try
        {
            return NativeInfo(in MemoryMarshal.GetReference(data), data.Length, out var w, out var h, out var f, out var c) == 0
                && w > 0 && h > 0
                ? new Info(w, h, f, c)
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Linear float RGBA (missing G/B = 0, missing A = 1, Y = grey).</summary>
    public static ExrFile.Frame? TryDecode(ReadOnlySpan<byte> data)
    {
        if (TryInfo(data) is not { } info)
        {
            return null;
        }

        var floats = (long)info.Width * info.Height * 4;
        if (floats > Array.MaxLength)
        {
            return null;
        }

        try
        {
            var rgba = new float[floats];
            return NativeDecode(in MemoryMarshal.GetReference(data), data.Length, ref rgba[0], floats) == 0
                ? new ExrFile.Frame(rgba, info.Width, info.Height, ExrFile.CompressionName(info.Compression) ?? "?")
                : null;
        }
        catch (OutOfMemoryException)
        {
            return null;
        }
    }
}
