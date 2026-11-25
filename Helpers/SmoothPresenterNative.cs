using System.Runtime.InteropServices;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Optional Diligent smooth-viewer host (<c>palace_smooth.dll</c>, x64).
/// Built on Windows CI / FENRIR-STUDIO via CMake FetchContent DiligentCore.
/// Absent on Linux Cloud Agents until the DLL is packaged beside the app.
/// </summary>
public static class SmoothPresenterNative
{
    public const string Lib = "palace_smooth";

    public enum StatusCode
    {
        Ok = 0,
        Failed = 1,
        MissingDiligent = 2,
        UnsupportedArch = 3,
        BadArgument = 4,
        NotAttached = 5,
        DeviceLost = 6,
        ResizeFailed = 7
    }

    public enum Backend
    {
        Auto = 0,
        D3D12 = 1,
        D3D11 = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FrameStats
    {
        public double WaitMs;
        public double RenderMs;
        public double PresentMs;
        public double TotalMs;
        public ulong FrameIndex;
        public uint Width;
        public uint Height;
        public int Backend;
    }

    private static readonly Lazy<string?> LoadedVersion = new(TryLoadVersion);

    public static bool IsAvailable => LoadedVersion.Value is not null;

    public static string? Version => LoadedVersion.Value;

    public static string AvailabilityCaption =>
        IsAvailable
            ? $"palace_smooth {Version}"
            : "palace_smooth.dll not built (cmake Native/smooth on x64 Windows)";

    public static bool IsSupportedArchitecture =>
        RuntimeInformation.ProcessArchitecture is Architecture.X64;

    public static string ArchitectureCaption =>
        $"process={RuntimeInformation.ProcessArchitecture}; OS={RuntimeInformation.OSArchitecture}";

    public static nint CreateHost(Backend backend = Backend.Auto)
    {
        if (!IsAvailable || !IsSupportedArchitecture)
        {
            return 0;
        }

        try
        {
            return NativeCreate((int)backend);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return 0;
        }
    }

    public static void DestroyHost(nint host)
    {
        if (host == 0 || !IsAvailable)
        {
            return;
        }

        try
        {
            NativeDestroy(host);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // ignore
        }
    }

    public static StatusCode CreateCompositionSwapChain(nint host, uint width, uint height, out nint dxgiSwapChain)
    {
        dxgiSwapChain = 0;
        if (host == 0 || !IsAvailable)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeCreateCompositionSwapChain(host, width, height, out dxgiSwapChain);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static StatusCode Resize(nint host, uint width, uint height)
    {
        if (host == 0 || !IsAvailable)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeResize(host, width, height);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static StatusCode SetDpiScale(nint host, float scaleX, float scaleY)
    {
        if (host == 0 || !IsAvailable)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeSetDpiScale(host, scaleX, scaleY);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static StatusCode RenderFrame(nint host)
    {
        if (host == 0 || !IsAvailable)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeRenderFrame(host);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static bool TryLastFrameStats(nint host, out FrameStats stats)
    {
        stats = default;
        if (host == 0 || !IsAvailable)
        {
            return false;
        }

        try
        {
            return NativeLastFrameStats(host, out stats) == (int)StatusCode.Ok;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    public static Backend GetBackend(nint host)
    {
        if (host == 0 || !IsAvailable)
        {
            return Backend.Auto;
        }

        try
        {
            return (Backend)NativeBackend(host);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Backend.Auto;
        }
    }

    public static string LastError(nint host)
    {
        if (host == 0 || !IsAvailable)
        {
            return AvailabilityCaption;
        }

        try
        {
            var len = NativeLastError(host, null, 0);
            if (len <= 0)
            {
                return "";
            }

            var buf = new byte[len];
            NativeLastError(host, buf, len);
            return Encoding.UTF8.GetString(buf).TrimEnd('\0');
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return AvailabilityCaption;
        }
    }

    private static string? TryLoadVersion()
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
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_version")]
    private static extern IntPtr NativeVersion();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_create")]
    private static extern nint NativeCreate(int backend);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_destroy")]
    private static extern void NativeDestroy(nint host);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_create_composition_swapchain")]
    private static extern int NativeCreateCompositionSwapChain(
        nint host,
        uint width,
        uint height,
        out nint dxgiSwapChain);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_resize")]
    private static extern int NativeResize(nint host, uint width, uint height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_set_dpi_scale")]
    private static extern int NativeSetDpiScale(nint host, float scaleX, float scaleY);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_render_frame")]
    private static extern int NativeRenderFrame(nint host);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_last_frame_stats")]
    private static extern int NativeLastFrameStats(nint host, out FrameStats stats);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_last_error")]
    private static extern int NativeLastError(nint host, [Out] byte[]? buffer, int bufferBytes);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_backend")]
    private static extern int NativeBackend(nint host);

    public static StatusCode SetShaderSearchDirs(nint host, string? utf8Dirs)
    {
        if (host == 0 || !IsAvailable)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeSetShaderSearchDirs(host, utf8Dirs);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static StatusCode TilePoolCreate(nint host, uint capacity)
    {
        if (host == 0 || !IsAvailable)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeTilePoolCreate(host, capacity);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static void TilePoolDestroy(nint host)
    {
        if (host == 0 || !IsAvailable)
        {
            return;
        }

        try
        {
            NativeTilePoolDestroy(host);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // ignore
        }
    }

    public static StatusCode TileUpload(nint host, uint slice, byte[] rgba16F)
    {
        if (host == 0 || !IsAvailable || rgba16F is null)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeTileUpload(host, slice, rgba16F, rgba16F.Length);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static StatusCode TileMipBox(
        nint host,
        uint levelLW,
        uint levelLH,
        int parentContentOriginL1X,
        int parentContentOriginL1Y,
        uint dstSlice,
        uint[] slices4x4)
    {
        if (host == 0 || !IsAvailable || slices4x4 is null || slices4x4.Length < 16)
        {
            return StatusCode.Failed;
        }

        try
        {
            return (StatusCode)NativeTileMipBox(
                host,
                levelLW,
                levelLH,
                parentContentOriginL1X,
                parentContentOriginL1Y,
                dstSlice,
                slices4x4);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return StatusCode.Failed;
        }
    }

    public static bool TileMipReady(nint host)
    {
        if (host == 0 || !IsAvailable)
        {
            return false;
        }

        try
        {
            return NativeTileMipReady(host) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_set_shader_search_dirs")]
    private static extern int NativeSetShaderSearchDirs(nint host, [MarshalAs(UnmanagedType.LPUTF8Str)] string? utf8Dirs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_tile_pool_create")]
    private static extern int NativeTilePoolCreate(nint host, uint capacity);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_tile_pool_destroy")]
    private static extern void NativeTilePoolDestroy(nint host);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_tile_upload")]
    private static extern int NativeTileUpload(nint host, uint slice, byte[] rgba16f, int byteCount);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_tile_mip_box")]
    private static extern int NativeTileMipBox(
        nint host,
        uint levelLW,
        uint levelLH,
        int parentOx,
        int parentOy,
        uint dstSlice,
        uint[] slices4x4);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "psv_tile_mip_ready")]
    private static extern int NativeTileMipReady(nint host);
}
