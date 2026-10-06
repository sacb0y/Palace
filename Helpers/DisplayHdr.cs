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

    public static DisplayHdrQuery LastQuery { get; private set; } =
        new("CCD not queried", null, 0, 0, null, null);

    /// <summary>
    /// One-line CCD + DXGI probe for Overlay Info / DebugView.
    /// </summary>
    public static string Describe(string? gdiDeviceName = null)
    {
        _ = TryWindowsHdrEnabled(gdiDeviceName);
        var text = LastQuery.Summary + " | " + DescribeDxgi();
        WriteDebug(text);
        return text;
    }

    public static string DescribeDxgi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "DXGI unavailable (not Windows)";
        }

        try
        {
            return QueryDxgiOutputs();
        }
        catch (Exception ex)
        {
            return "DXGI probe failed: " + ex.GetType().Name;
        }
    }

    public static void WriteDebug(string text)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            OutputDebugString("Palace.DisplayHdr " + text);
        }
        catch
        {
            // DebugView is optional.
        }
    }

    /// <summary>
    /// <c>true</c> when Windows HDR / Advanced Color is on for
    /// <paramref name="gdiDeviceName"/> (<c>\\.\DISPLAYn</c>). Empty name
    /// uses any active path. <c>null</c> when DisplayConfig is unavailable.
    /// </summary>
    public static bool? TryWindowsHdrEnabled(string? gdiDeviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            LastQuery = new("CCD unavailable (not Windows)", null, 0, 0, null, gdiDeviceName);
            return null;
        }

        try
        {
            return QueryWindowsHdrEnabled(gdiDeviceName);
        }
        catch (Exception ex)
        {
            LastQuery = new("CCD throw " + ex.GetType().Name, null, 0, 0, null, gdiDeviceName);
            return null;
        }
    }

    private static bool? QueryWindowsHdrEnabled(string? gdiDeviceName)
    {
        DisplayHdrQuery? last = null;
        foreach (var flags in new uint[] { 0x12, 0x52, 0x02, 0x01 })
        {
            var parsed = TryQueryPaths(flags, gdiDeviceName, out var hdr, out var query);
            last = query;
            if (parsed)
            {
                LastQuery = query;
                WriteDebug(query.Summary);
                return hdr;
            }
        }

        LastQuery = last ?? new("CCD no path flags", null, 0, 0, null, gdiDeviceName);
        WriteDebug(LastQuery.Summary);
        return null;
    }

    private static bool TryQueryPaths(
        uint flags,
        string? gdiDeviceName,
        out bool? hdr,
        out DisplayHdrQuery query)
    {
        hdr = null;
        query = new($"CCD flags=0x{flags:X} bufHr=? paths=0", null, 0, 0, null, gdiDeviceName);
        var pathCount = 0u;
        var modeCount = 0u;
        var bufHr = GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount);
        if (bufHr != 0 || pathCount == 0 || modeCount == 0)
        {
            query = new(
                $"CCD flags=0x{flags:X} bufHr={bufHr} paths={pathCount} modes={modeCount}",
                null,
                (int)pathCount,
                0,
                null,
                gdiDeviceName);
            return false;
        }

        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        var qHr = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (qHr != 0)
        {
            query = new(
                $"CCD flags=0x{flags:X} bufHr=0 paths={pathCount} qHr={qHr}",
                null,
                (int)pathCount,
                0,
                null,
                gdiDeviceName);
            return false;
        }

        bool? anyHdr = null;
        var matches = 0;
        bool? matched = null;
        bool? firstAce = null;
        string? firstSource = null;
        uint firstInfo = 0;
        int firstInfoHr = -1;
        uint firstInfo2 = 0;
        int firstInfo2Hr = -1;
        int firstMode = -1;
        var want = string.IsNullOrWhiteSpace(gdiDeviceName) ? null : gdiDeviceName.Trim();
        for (var i = 0; i < (int)pathCount; i++)
        {
            var path = paths[i];
            var detail = ReadPathHdr(path.targetInfo.adapterId, path.targetInfo.id);
            if (firstAce is null)
            {
                firstAce = detail.AdvancedColorEnabled;
                firstInfo = detail.InfoValue;
                firstInfoHr = detail.InfoHr;
                firstInfo2 = detail.Info2Value;
                firstInfo2Hr = detail.Info2Hr;
                firstMode = detail.ColorMode;
            }

            if (detail.Hdr == true)
            {
                anyHdr = true;
            }
            else if (detail.Hdr == false && anyHdr is null)
            {
                anyHdr = false;
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
            var srcHr = DisplayConfigGetDeviceInfo(ref source);
            var srcName = srcHr == 0 ? source.viewGdiDeviceName : null;
            firstSource ??= srcName;
            if (want is null)
            {
                continue;
            }

            if (srcHr != 0 || !NamesMatch(srcName, want))
            {
                continue;
            }

            matches++;
            matched = detail.Hdr;
            firstAce = detail.AdvancedColorEnabled;
            firstInfo = detail.InfoValue;
            firstInfoHr = detail.InfoHr;
            firstInfo2 = detail.Info2Value;
            firstInfo2Hr = detail.Info2Hr;
            firstMode = detail.ColorMode;
            firstSource = srcName;
        }

        if (want is null)
        {
            hdr = anyHdr;
        }
        else if (matches == 1)
        {
            hdr = matched ?? anyHdr;
        }
        else
        {
            hdr = anyHdr == true ? true : matches == 0 ? anyHdr : matched;
        }

        query = new(
            $"CCD flags=0x{flags:X} qHr=0 paths={pathCount} want={want ?? "-"} match={matches} src={firstSource ?? "-"} ACE={FmtFlag(firstAce)} info=0x{firstInfo:X}/hr{firstInfoHr} info2=0x{firstInfo2:X}/hr{firstInfo2Hr} mode={firstMode} winHdr={FmtFlag(hdr)}",
            hdr,
            (int)pathCount,
            matches,
            firstAce,
            want ?? firstSource);
        return hdr is not null && (want is null || matches == 1 || hdr == true);
    }

    private static PathHdr ReadPathHdr(Luid adapterId, uint targetId)
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
        var info2Hr = DisplayConfigGetDeviceInfo(ref info2);
        bool? hdr = null;
        if (info2Hr == 0)
        {
            hdr = CombineWindowsHdr(
                WindowsHdrEnabledFromInfo2(info2.value),
                info2.activeColorMode,
                WindowsHdrEnabledFromInfo(info2.value)
                    || AdvancedColorActiveFromInfo2(info2.value));
            if (hdr == true)
            {
                return new(
                    true,
                    WindowsHdrEnabledFromInfo(info2.value) || AdvancedColorActiveFromInfo2(info2.value),
                    info2.value,
                    -1,
                    info2.value,
                    info2Hr,
                    info2.activeColorMode);
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
        var infoHr = DisplayConfigGetDeviceInfo(ref info);
        var ace = infoHr == 0 ? WindowsHdrEnabledFromInfo(info.value) : (bool?)null;
        hdr ??= ace;
        return new(hdr, ace, infoHr == 0 ? info.value : 0, infoHr, info2.value, info2Hr, info2.activeColorMode);
    }

    private static string FmtFlag(bool? value) =>
        value is null ? "?" : value.Value ? "1" : "0";

    private static string QueryDxgiOutputs()
    {
        var iid = IidDxgiFactory1;
        var hr = CreateDXGIFactory1(ref iid, out var factory);
        if (hr < 0 || factory == IntPtr.Zero)
        {
            return $"DXGI factoryHr=0x{hr:X8}";
        }

        try
        {
            var parts = new List<string>();
            for (uint a = 0; a < 8; a++)
            {
                var adapter = CallEnumAdapters(factory, a);
                if (adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    for (uint o = 0; o < 8; o++)
                    {
                        var output = CallEnumOutputs(adapter, o);
                        if (output == IntPtr.Zero)
                        {
                            break;
                        }

                        try
                        {
                            var output6 = QueryInterface(output, IidDxgiOutput6);
                            if (output6 == IntPtr.Zero)
                            {
                                parts.Add($"out{a}.{o} noOutput6");
                                continue;
                            }

                            try
                            {
                                var desc = new DxgiOutputDesc1();
                                var dhr = CallGetDesc1(output6, ref desc);
                                if (dhr < 0)
                                {
                                    parts.Add($"out{a}.{o} descHr=0x{dhr:X8}");
                                    continue;
                                }

                                parts.Add(
                                    $"DXGI {desc.DeviceName} cs={desc.ColorSpace} max={desc.MaxLuminance:0} ff={desc.MaxFullFrameLuminance:0} bits={desc.BitsPerColor}");
                            }
                            finally
                            {
                                Release(output6);
                            }
                        }
                        finally
                        {
                            Release(output);
                        }
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }

            return parts.Count == 0 ? "DXGI no outputs" : string.Join(" · ", parts);
        }
        finally
        {
            Release(factory);
        }
    }

    private static IntPtr CallEnumAdapters(IntPtr factory, uint index)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<EnumAdaptersDelegate>(Vtbl(factory, 7));
        var hr = fn(factory, index, out var adapter);
        return hr < 0 ? IntPtr.Zero : adapter;
    }

    private static IntPtr CallEnumOutputs(IntPtr adapter, uint index)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<EnumOutputsDelegate>(Vtbl(adapter, 7));
        var hr = fn(adapter, index, out var output);
        return hr < 0 ? IntPtr.Zero : output;
    }

    private static int CallGetDesc1(IntPtr output6, ref DxgiOutputDesc1 desc)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(Vtbl(output6, 27));
        return fn(output6, ref desc);
    }

    private static IntPtr QueryInterface(IntPtr unk, Guid iid)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(Vtbl(unk, 0));
        var hr = fn(unk, ref iid, out var ppv);
        return hr < 0 ? IntPtr.Zero : ppv;
    }

    private static void Release(IntPtr unk)
    {
        if (unk == IntPtr.Zero)
        {
            return;
        }

        var fn = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(Vtbl(unk, 2));
        fn(unk);
    }

    private static IntPtr Vtbl(IntPtr unk, int index)
    {
        var table = Marshal.ReadIntPtr(unk);
        return Marshal.ReadIntPtr(table, index * IntPtr.Size);
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "OutputDebugStringW")]
    private static extern void OutputDebugString(string message);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiOutputDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public int DesktopLeft;
        public int DesktopTop;
        public int DesktopRight;
        public int DesktopBottom;
        public int AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
        public uint BitsPerColor;
        public int ColorSpace;
        public float RedPrimaryX;
        public float RedPrimaryY;
        public float GreenPrimaryX;
        public float GreenPrimaryY;
        public float BluePrimaryX;
        public float BluePrimaryY;
        public float WhitePointX;
        public float WhitePointY;
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    private readonly record struct PathHdr(
        bool? Hdr,
        bool? AdvancedColorEnabled,
        uint InfoValue,
        int InfoHr,
        uint Info2Value,
        int Info2Hr,
        int ColorMode);

    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IidDxgiOutput6 = new("068346e8-aaec-4b84-add5-13ff8c7033c8");

    private delegate int EnumAdaptersDelegate(IntPtr self, uint index, out IntPtr adapter);
    private delegate int EnumOutputsDelegate(IntPtr self, uint index, out IntPtr output);
    private delegate int GetDesc1Delegate(IntPtr self, ref DxgiOutputDesc1 desc);
    private delegate int QueryInterfaceDelegate(IntPtr self, ref Guid iid, out IntPtr ppv);
    private delegate uint ReleaseDelegate(IntPtr self);
}

public readonly record struct DisplayHdrQuery(
    string Summary,
    bool? WindowsHdr,
    int PathCount,
    int NameMatches,
    bool? AdvancedColorEnabled,
    string? DeviceName);
