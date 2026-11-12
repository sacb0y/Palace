using System.Runtime.InteropServices;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Optional Diligent D3D12 host (<c>palace_mesh3d.dll</c>, x64). Built by
/// <c>tools/mesh3d/build.ps1</c> against DiligentCore / Tools / FX. Absent on
/// Linux CI and until a Windows build drops the DLL next to the app.
/// </summary>
public static class Mesh3dNative
{
    public const string Lib = "palace_mesh3d";

    public enum StatusCode
    {
        Ok = 0,
        Failed = 1,
        MissingDiligent = 2,
        UnsupportedArch = 3,
        BadArgument = 4,
        NotAttached = 5,
        LoadFailed = 6
    }

    private static readonly Lazy<string?> LoadedVersion = new(TryLoadVersion);

    public static bool IsAvailable => LoadedVersion.Value is not null;

    public static string? Version => LoadedVersion.Value;

    public static string AvailabilityCaption =>
        IsAvailable
            ? $"palace_mesh3d {Version}"
            : "palace_mesh3d.dll not built (tools/mesh3d/build.ps1 on x64 Windows)";

    public static nint CreateHost()
    {
        if (!IsAvailable)
        {
            return 0;
        }

        try
        {
            return NativeCreate();
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

    public static StatusCode LoadGltf(nint host, string path)
    {
        if (host == 0 || !IsAvailable || string.IsNullOrWhiteSpace(path))
        {
            return StatusCode.BadArgument;
        }

        try
        {
            return (StatusCode)NativeLoadGltf(host, path);
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

    public static Mesh3dStreamlineProbe ProbeStreamline(nint host)
    {
        if (!IsAvailable)
        {
            return Mesh3dStreamlineProbe.Unavailable(AvailabilityCaption);
        }

        if (host == 0)
        {
            return Mesh3dStreamlineProbe.Unavailable("host not created");
        }

        try
        {
            var hr = NativeProbeStreamline(host, out var device, out var commandList);
            if (hr != (int)StatusCode.Ok)
            {
                return Mesh3dStreamlineProbe.Unavailable(
                    $"native probe status {(StatusCode)hr}: {LastError(host)}");
            }

            return Mesh3dStreamlineProbe.FromPointers(device, commandList, LastError(host));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Mesh3dStreamlineProbe.Unavailable(ex.GetType().Name);
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
            var text = Encoding.UTF8.GetString(buf).TrimEnd('\0');
            return text;
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

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_version")]
    private static extern IntPtr NativeVersion();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_create")]
    private static extern nint NativeCreate();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_destroy")]
    private static extern void NativeDestroy(nint host);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_create_composition_swapchain")]
    private static extern int NativeCreateCompositionSwapChain(
        nint host,
        uint width,
        uint height,
        out nint dxgiSwapChain);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_resize")]
    private static extern int NativeResize(nint host, uint width, uint height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, EntryPoint = "pm3_load_gltf")]
    private static extern int NativeLoadGltf(nint host, [MarshalAs(UnmanagedType.LPWStr)] string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_render_frame")]
    private static extern int NativeRenderFrame(nint host);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_probe_streamline")]
    private static extern int NativeProbeStreamline(nint host, out nint device, out nint commandList);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pm3_last_error")]
    private static extern int NativeLastError(nint host, [Out] byte[]? buffer, int bufferBytes);
}
