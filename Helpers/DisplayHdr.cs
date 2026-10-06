using System.Runtime.InteropServices;

namespace Palace.Helpers;

/// <summary>
/// Windows HDR (Advanced Color) from DisplayConfig. DXGI ColorSpace often
/// stays G22 with dummy 270 nits while HDR is on — that is not SDR.
/// Off WinRT. Linux tests only parse flags.
/// </summary>
public static class DisplayHdr
{
    public const int ColorModeSdr = 0;
    public const int ColorModeWcg = 1;
    public const int ColorModeHdr = 2;

    /// <summary>
    /// Win10 <c>DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO</c> bit 1
    /// (<c>advancedColorEnabled</c>) is the HDR toggle before ACM split.
    /// </summary>
    public static bool WindowsHdrEnabledFromInfo(uint value) =>
        (value & 2u) != 0;

    /// <summary>
    /// Win11 <c>DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2</c>: bit 5 is
    /// <c>highDynamicRangeUserEnabled</c>. Do not use bit 1
    /// (<c>advancedColorActive</c>) — that is also WCG / ACM SDR.
    /// </summary>
    public static bool WindowsHdrEnabledFromInfo2(uint value) =>
        ((value >> 5) & 1u) != 0;

    public static bool IsHdrColorMode(int activeColorMode) =>
        activeColorMode == ColorModeHdr;

    public static bool? CombineWindowsHdr(
        bool? info2HdrUserEnabled,
        int? info2ColorMode,
        bool? infoEnabled)
    {
        if (info2HdrUserEnabled == true || (info2ColorMode is { } mode && IsHdrColorMode(mode)))
        {
            return true;
        }

        if (info2HdrUserEnabled == false && info2ColorMode is { } known && known != ColorModeHdr)
        {
            return false;
        }

        return infoEnabled;
    }

    /// <summary>
    /// <c>true</c> when Windows HDR is on for <paramref name="gdiDeviceName"/>
    /// (<c>\\.\DISPLAYn</c>). <c>null</c> when DisplayConfig is unavailable
    /// (Linux tests) or the name does not match an active path.
    /// </summary>
    public static bool? TryWindowsHdrEnabled(string? gdiDeviceName)
    {
        if (string.IsNullOrWhiteSpace(gdiDeviceName) || !OperatingSystem.IsWindows())
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

    private static bool? QueryWindowsHdrEnabled(string gdiDeviceName)
    {
        const uint onlyActive = 2;
        var pathCount = 0u;
        var modeCount = 0u;
        if (GetDisplayConfigBufferSizes(onlyActive, out pathCount, out modeCount) != 0
            || pathCount == 0)
        {
            return null;
        }

        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(onlyActive, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
        {
            return null;
        }

        bool? anyHdr = null;
        var matches = 0;
        bool? matched = null;
        for (var i = 0; i < (int)pathCount; i++)
        {
            var path = paths[i];
            var source = new SourceName
            {
                header = new DeviceInfoHeader
                {
                    type = 1,
                    size = (uint)Marshal.SizeOf<SourceName>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0)
            {
                continue;
            }

            var hdr = PathWindowsHdr(path.targetInfo.adapterId, path.targetInfo.id);
            if (hdr == true)
            {
                anyHdr = true;
            }
            else if (hdr == false && anyHdr is null)
            {
                anyHdr = false;
            }

            if (!NamesMatch(source.viewGdiDeviceName, gdiDeviceName))
            {
                continue;
            }

            matches++;
            matched = hdr;
        }

        if (matches == 1)
        {
            return matched;
        }

        return matches == 0 ? anyHdr : matched;
    }

    private static bool? PathWindowsHdr(Luid adapterId, uint targetId)
    {
        var info2 = new AdvancedColorInfo2
        {
            header = new DeviceInfoHeader
            {
                type = 15,
                size = (uint)Marshal.SizeOf<AdvancedColorInfo2>(),
                adapterId = adapterId,
                id = targetId
            }
        };
        if (DisplayConfigGetDeviceInfo(ref info2) == 0)
        {
            return CombineWindowsHdr(
                WindowsHdrEnabledFromInfo2(info2.value),
                info2.activeColorMode,
                WindowsHdrEnabledFromInfo(info2.value));
        }

        var info = new AdvancedColorInfo
        {
            header = new DeviceInfoHeader
            {
                type = 9,
                size = (uint)Marshal.SizeOf<AdvancedColorInfo>(),
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

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int type;
        public uint size;
        public Luid adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public DeviceInfoHeader header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo2
    {
        public DeviceInfoHeader header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
        public int activeColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
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

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public PathSourceInfo sourceInfo;
        public PathTargetInfo targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
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
