using System.Runtime.InteropServices;

namespace Palace.Helpers;

/// <summary>
/// Windows HDR (Advanced Color) from DisplayConfig. DXGI ColorSpace often
/// stays G22 with dummy 270 nits while HDR is on — that is not SDR.
/// Native CCD structs are 4-byte packed (header is 20 bytes). Off WinRT.
/// Linux tests only parse flags / <see cref="NativeLayoutSizes"/>.
/// </summary>
public static class DisplayHdr
{
    public const int ColorModeSdr = 0;
    public const int ColorModeWcg = 1;
    public const int ColorModeHdr = 2;

    public const int NativeHeaderBytes = 20;
    public const int NativePathBytes = 72;
    public const int NativeModeBytes = 64;
    public const int NativeInfoBytes = 32;
    public const int NativeInfo2Bytes = 36;
    public const int NativeSourceNameBytes = 84;

    /// <summary>
    /// Win10/11 <c>DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO</c> bit 1
    /// (<c>advancedColorEnabled</c>). Isiac’s SAM713F reports this as 1
    /// with <c>HDREnabled=1</c> while DXGI stays G22 / dummy 270.
    /// </summary>
    public static bool WindowsHdrEnabledFromInfo(uint value) =>
        (value & 2u) != 0;

    /// <summary>
    /// Win11 <c>INFO_2</c> bit 5 <c>highDynamicRangeUserEnabled</c>.
    /// </summary>
    public static bool WindowsHdrEnabledFromInfo2(uint value) =>
        ((value >> 5) & 1u) != 0;

    /// <summary>
    /// Win11 <c>INFO_2</c> bit 1 <c>advancedColorActive</c> plus HDR user
    /// or HDR mode. Active-only is WCG; still honor INFO
    /// <c>advancedColorEnabled</c>.
    /// </summary>
    public static bool AdvancedColorActiveFromInfo2(uint value) =>
        (value & 2u) != 0;

    public static bool IsHdrColorMode(int activeColorMode) =>
        activeColorMode == ColorModeHdr;

    /// <summary>
    /// HDR when the user HDR toggle is on, INFO_2 mode is HDR, or
    /// <c>AdvancedColorEnabled</c> is set. Do not treat a failed parse as
    /// “HDR off” — that forced SDR on Main-Desktop.
    /// </summary>
    public static bool? CombineWindowsHdr(
        bool? info2HdrUserEnabled,
        int? info2ColorMode,
        bool? infoEnabled)
    {
        if (info2HdrUserEnabled == true || (info2ColorMode is { } mode && IsHdrColorMode(mode)))
        {
            return true;
        }

        if (infoEnabled == true)
        {
            return true;
        }

        if (info2HdrUserEnabled == false && info2ColorMode is { } known && known != ColorModeHdr)
        {
            return false;
        }

        return infoEnabled;
    }

    public static (int Header, int Path, int Mode, int Info, int Info2, int SourceName) NativeLayoutSizes() =>
        (
            Marshal.SizeOf<DeviceInfoHeader>(),
            Marshal.SizeOf<PathInfo>(),
            Marshal.SizeOf<ModeInfo>(),
            Marshal.SizeOf<AdvancedColorInfo>(),
            Marshal.SizeOf<AdvancedColorInfo2>(),
            Marshal.SizeOf<SourceName>());

    /// <summary>
    /// <c>true</c> when Windows HDR / Advanced Color is on for
    /// <paramref name="gdiDeviceName"/> (<c>\\.\DISPLAYn</c>). Empty name
    /// uses any active path. <c>null</c> when DisplayConfig is unavailable.
    /// </summary>
    public static bool? TryWindowsHdrEnabled(string? gdiDeviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return QueryWindowsHdrEnabled(gdiDeviceName);
        }
        catch
        {
            return null;
        }
    }

    private static bool? QueryWindowsHdrEnabled(string? gdiDeviceName)
    {
        foreach (var flags in new uint[] { 0x12, 0x52, 0x02, 0x01 })
        {
            var parsed = TryQueryPaths(flags, gdiDeviceName, out var hdr);
            if (parsed)
            {
                return hdr;
            }
        }

        return null;
    }

    private static bool TryQueryPaths(uint flags, string? gdiDeviceName, out bool? hdr)
    {
        hdr = null;
        var pathCount = 0u;
        var modeCount = 0u;
        if (GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount) != 0
            || pathCount == 0
            || modeCount == 0)
        {
            return false;
        }

        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
        {
            return false;
        }

        bool? anyHdr = null;
        var matches = 0;
        bool? matched = null;
        var want = string.IsNullOrWhiteSpace(gdiDeviceName) ? null : gdiDeviceName.Trim();
        for (var i = 0; i < (int)pathCount; i++)
        {
            var path = paths[i];
            var pathHdr = PathWindowsHdr(path.targetInfo.adapterId, path.targetInfo.id);
            if (pathHdr == true)
            {
                anyHdr = true;
            }
            else if (pathHdr == false && anyHdr is null)
            {
                anyHdr = false;
            }

            if (want is null)
            {
                continue;
            }

            var source = new SourceName
            {
                header = new DeviceInfoHeader
                {
                    type = 1,
                    size = NativeSourceNameBytes,
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0
                || !NamesMatch(source.viewGdiDeviceName, want))
            {
                continue;
            }

            matches++;
            matched = pathHdr;
        }

        if (want is null)
        {
            hdr = anyHdr;
            return anyHdr is not null;
        }

        if (matches == 1)
        {
            hdr = matched ?? anyHdr;
            return true;
        }

        hdr = anyHdr;
        return anyHdr == true;
    }

    private static bool? PathWindowsHdr(Luid adapterId, uint targetId)
    {
        var info2 = new AdvancedColorInfo2
        {
            header = new DeviceInfoHeader
            {
                type = 15,
                size = NativeInfo2Bytes,
                adapterId = adapterId,
                id = targetId
            }
        };
        if (DisplayConfigGetDeviceInfo(ref info2) == 0)
        {
            var combined = CombineWindowsHdr(
                WindowsHdrEnabledFromInfo2(info2.value),
                info2.activeColorMode,
                WindowsHdrEnabledFromInfo(info2.value)
                    || AdvancedColorActiveFromInfo2(info2.value));
            if (combined == true)
            {
                return true;
            }
        }

        var info = new AdvancedColorInfo
        {
            header = new DeviceInfoHeader
            {
                type = 9,
                size = NativeInfoBytes,
                adapterId = adapterId,
                id = targetId
            }
        };
        if (DisplayConfigGetDeviceInfo(ref info) != 0)
        {
            return null;
        }

        return WindowsHdrEnabledFromInfo(info.value);
    }

    private static bool NamesMatch(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        var a = left.Trim();
        var b = right.Trim();
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return a.EndsWith(b, StringComparison.OrdinalIgnoreCase)
            || b.EndsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] PathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] ModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo2 request);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DeviceInfoHeader
    {
        public int type;
        public uint size;
        public Luid adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct AdvancedColorInfo
    {
        public DeviceInfoHeader header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct AdvancedColorInfo2
    {
        public DeviceInfoHeader header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
        public int activeColorMode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PathSourceInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PathTargetInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public Rational refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PathInfo
    {
        public PathSourceInfo sourceInfo;
        public PathTargetInfo targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ModeInfo
    {
        public uint infoType;
        public uint id;
        public Luid adapterId;
        public ulong pixelRate;
        public Rational hSyncFreq;
        public Rational vSyncFreq;
        public uint activeCx;
        public uint activeCy;
        public uint totalCx;
        public uint totalCy;
        public uint videoStandard;
        public uint scanLineOrdering;
    }
}
