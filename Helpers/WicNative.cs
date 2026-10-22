using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Palace.Helpers;

/// <summary>
/// Native WIC IUnknown interfaces for HDR JPEG XR. <c>[ComImport]</c>
/// does <strong>not</strong> copy C# base methods onto a derived vtable —
/// <c>IWICBitmapScaler : IWICBitmapSource</c> would put
/// <c>Initialize</c> on <c>GetSize</c>. Flatten the BitmapSource slots
/// on every derived interface. No Magick.
/// </summary>
public static class WicNative
{
    public static readonly string[] BitmapSourceMethods =
    [
        "GetSize",
        "GetPixelFormat",
        "GetResolution",
        "CopyPalette",
        "CopyPixels"
    ];

    public static string[] DeclaredMethods(Type type) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(m => m.MetadataToken)
            .Select(static m => m.Name)
            .ToArray();

    public static IWICBitmapSource AsSource(object com) => (IWICBitmapSource)com;

    /// <summary>
    /// Packaged WinUI: the WIC catalog omits HD Photo
    /// (<c>0x88982F50</c>). <c>CoCreate(CLSID_WICWmpDecoder)</c> and
    /// <c>CreateAsync(JpegXrDecoderId)</c> both QI
    /// <c>E_NOINTERFACE</c> (<c>0x80004002</c>); some WASDK builds
    /// lack <c>BitmapDecoder.JpegXrDecoderId</c>. BitmapImage /
    /// <c>CreateAsync(stream)</c> still opens these files. Packaged
    /// HDR present: native float CopyPixels first (filename / handle /
    /// memory), then <c>CoCreate</c> / <c>DllGetClassObject</c>
    /// <c>CLSID_WICWmpDecoder</c> + <c>Initialize</c> (<c>wmp</c>) when
    /// the packaged factory catalog omits HD Photo, then QI the WinRT
    /// decoder/frame (<c>IWinRTObject.ThisPtr</c> +
    /// <c>QueryInterface</c>) for float/half CopyPixels, then
    /// <c>CreateStreamOverRandomAccessStream</c> (<c>rasStream</c> /
    /// <c>rasDecoder</c>). WinRT unorm (<c>Rgba16</c>) clamps HDR — do
    /// not present it. Stamp the exact call on <c>LastWicError</c>;
    /// chain prior QI / unorm with RAS via <see cref="Join"/>.
    /// </summary>
    public static class WicDecoderOpen
    {
        public const string Filename = "filename";
        public const string Handle = "handle";
        public const string Memory = "memory";
        public const string Wmp = "wmp";
        public const string CreateAsync = "CreateAsync";
        public const string Qi = "qi";
        public const string Ras = "ras";
        public const string RasStream = "rasStream";
        public const string RasDecoder = "rasDecoder";
        public const string GetSoftwareBitmap = "GetSoftwareBitmap";
        public const string LockBuffer = "LockBuffer";
        public const string GetPixelData = "GetPixelData";
        public const string UnormClamp = "unorm";
        public const string Packaged = "packaged";
        public const uint ComponentNotFound = 0x88982F50;

        /// <summary>WIC could not decode the stream (missing codec / bad payload).</summary>
        public const uint BadImage = 0x88982F60;
        public const uint NoInterface = 0x80004002;
        public const uint WrongThread = 0x8001010E;
        public const uint Fail = 0x80004005;

        public static readonly string[] WinrtCalls =
        [
            CreateAsync,
            Qi,
            Ras,
            RasStream,
            RasDecoder,
            GetSoftwareBitmap,
            LockBuffer,
            GetPixelData,
            UnormClamp,
            Packaged
        ];

        public static readonly string[] Stages =
        [
            Filename,
            Handle,
            Memory,
            Wmp,
            CreateAsync,
            Qi,
            Ras,
            RasStream,
            RasDecoder,
            GetSoftwareBitmap,
            LockBuffer,
            GetPixelData,
            UnormClamp,
            Packaged
        ];

        public static string Failed(string stage, int hr = 0)
        {
            var suffix = hr == 0 ? "" : " " + unchecked((uint)hr).ToString("X8");
            return "WIC decoder " + stage + suffix;
        }

        public static string Failed(string stage, Exception ex) =>
            Failed(stage, ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005));

        /// <summary>
        /// Keep the QI / unorm stamp when RAS also fails so Display
        /// shows both (RAS used to overwrite QI).
        /// </summary>
        public static string Join(string? prior, string next)
        {
            if (string.IsNullOrEmpty(prior))
            {
                return next;
            }

            if (string.IsNullOrEmpty(next) || prior == next)
            {
                return prior!;
            }

            return prior + "; " + next;
        }

        /// <summary>
        /// Packaged float HDR JXR wall: <c>wmp</c> + <c>qi</c> both
        /// <c>E_NOINTERFACE</c>, <c>rasDecoder</c> <c>E_FAIL</c>.
        /// WinRT / <c>BitmapImage</c> only yield unorm SDR — no more
        /// CLSID retries. Ship jxrlib/Magick or present SDR preview.
        /// </summary>
        public static bool IsPackagedNoFloatWall(string? error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return false;
            }

            return error.Contains(Wmp, StringComparison.Ordinal)
                && error.Contains(Qi, StringComparison.Ordinal)
                && (error.Contains(RasDecoder, StringComparison.Ordinal)
                    || error.Contains(Ras, StringComparison.Ordinal))
                && error.Contains(NoInterface.ToString("X8"), StringComparison.Ordinal)
                && error.Contains(Fail.ToString("X8"), StringComparison.Ordinal);
        }

        /// <summary>
        /// Prefix the diagnostic chain with <c>packaged</c> so Info
        /// reads as a capability wall, not another CLSID miss.
        /// </summary>
        public static string MarkPackagedWall(string chain)
        {
            var marker = Failed(Packaged);
            return chain.StartsWith(marker, StringComparison.Ordinal)
                ? chain
                : Join(marker, chain);
        }

        public static bool IsComponentNotFound(int hr) =>
            unchecked((uint)hr) == ComponentNotFound;

        public static bool IsBadImage(int hr) =>
            unchecked((uint)hr) == BadImage;

        /// <summary>GUID_ContainerFormatHeif — Store HEIF Image Extension.</summary>
        public static readonly Guid ContainerFormatHeif =
            new("e1e62521-6787-47f9-85c0-6ce9c6d9e0e5");

        /// <summary>
        /// Paths that may lack a Store WIC codec (.jxl / .heic / .heif).
        /// Used only to decide whether to <em>probe</em> for a codec —
        /// not as a blanket BADIMAGE silence.
        /// </summary>
        public static bool IsStoreCodecOptionalPath(string? path) =>
            PathSafe.IsJxl(path) || PathSafe.IsHeif(path);

        /// <summary>
        /// Test override for <see cref="IsHeifWicDecoderMissing"/> (null =
        /// live probe / platform default).
        /// </summary>
        public static bool? HeifWicDecoderMissingOverride { get; set; }

        /// <summary>
        /// Test hook replacing the live COM probe. Throw to exercise leave-unset.
        /// </summary>
        internal static Func<bool>? HeifWicDecoderProbeHook { get; set; }

        private static int _heifDecoderMissing; // 0 unset, 1 missing, -1 present
        private static int _heifProbeFailures;
        private const int MaxHeifProbeFailures = 3;
        private const string HeifProbeLogKey = "heif-wic-decoder-probe";
        private static readonly ErrorThrottle HeifProbeLogThrottle =
            new(TimeSpan.FromSeconds(30));

        /// <summary>Clears probe cache, failure count, and log throttle (tests).</summary>
        internal static void ResetHeifWicDecoderMissingCache()
        {
            _heifDecoderMissing = 0;
            Volatile.Write(ref _heifProbeFailures, 0);
            HeifProbeLogThrottle.Reset();
        }

        /// <summary>
        /// True when no WIC HEIF decoder is registered (Store HEIF extension
        /// absent). Cached after the first probe. Non-Windows → missing.
        /// A probe throw leaves the cache unset (returns false) so corrupt HEIF
        /// is not silenced; logs are throttled; after
        /// <see cref="MaxHeifProbeFailures"/> failures the session stops probing
        /// but still returns false.
        /// </summary>
        public static bool IsHeifWicDecoderMissing()
        {
            if (HeifWicDecoderMissingOverride is { } forced)
            {
                return forced;
            }

            if (_heifDecoderMissing != 0)
            {
                return _heifDecoderMissing > 0;
            }

            // Stop probing for the session after N failures; still not-missing.
            if (Volatile.Read(ref _heifProbeFailures) >= MaxHeifProbeFailures)
            {
                return false;
            }

            // Hook lets Linux tests throw without Windows COM; live path needs OS.
            if (HeifWicDecoderProbeHook is null && !OperatingSystem.IsWindows())
            {
                _heifDecoderMissing = 1;
                return true;
            }

            try
            {
                _heifDecoderMissing = RunHeifWicDecoderProbe() ? 1 : -1;
            }
            catch (Exception ex)
            {
                // Leave unset — do not cache "missing" (that silenced corrupt HEIF).
                Interlocked.Increment(ref _heifProbeFailures);
                if (HeifProbeLogThrottle.ShouldReport(HeifProbeLogKey))
                {
                    ErrorReporter.LogOnly("HEIF WIC decoder probe", ex);
                }

                return false;
            }

            return _heifDecoderMissing > 0;
        }

        private static bool RunHeifWicDecoderProbe() =>
            HeifWicDecoderProbeHook is { } hook
                ? hook()
                : ProbeHeifWicDecoderMissing();

        /// <summary>
        /// <c>IWICImagingFactory.CreateDecoder(HEIF)</c> → COMPONENTNOTFOUND
        /// when the Store HEIF codec is absent. Call only after
        /// <see cref="OperatingSystem.IsWindows"/>.
        /// </summary>
#pragma warning disable CA1416 // Linked into Palace.Tests on Linux; gated by IsWindows.
        private static bool ProbeHeifWicDecoderMissing()
        {
            // CLSID_WICImagingFactory / Factory2 (same as HdrWicDecode).
            foreach (var clsid in new[]
                     {
                         new Guid("317d06e8-5f24-433d-bdf7-79ce68d8abc2"),
                         new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a")
                     })
            {
                var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
                if (type is null
                    || Activator.CreateInstance(type) is not IWICImagingFactory factory)
                {
                    continue;
                }

                try
                {
                    var fmt = ContainerFormatHeif;
                    var hr = factory.CreateDecoder(ref fmt, IntPtr.Zero, out var decoder);
                    if (decoder is not null)
                    {
                        Marshal.ReleaseComObject(decoder);
                    }

                    return IsComponentNotFound(hr);
                }
                finally
                {
                    Marshal.ReleaseComObject(factory);
                }
            }

            return true;
        }
#pragma warning restore CA1416

        /// <summary>
        /// Expected miss when falling through to WIC without a Store codec —
        /// stamp Display, do not LogOnly as an app error.
        /// <see cref="ComponentNotFound"/> always.
        /// <see cref="BadImage"/> only when the Store codec is actually
        /// missing: always for .jxl (present uses libjxl); for .heic/.heif
        /// only when <see cref="IsHeifWicDecoderMissing"/>. Corrupt HEIF
        /// with the codec installed still LogOnly.
        /// </summary>
        public static bool IsExpectedPresentMiss(int hr, string? path = null)
        {
            if (IsComponentNotFound(hr))
            {
                return true;
            }

            if (!IsBadImage(hr))
            {
                return false;
            }

            // JXL present never uses WIC (libjxl); BADIMAGE here is noise.
            if (PathSafe.IsJxl(path))
            {
                return true;
            }

            // HEIF: silence BADIMAGE only when the Store codec is absent.
            if (PathSafe.IsHeif(path))
            {
                return IsHeifWicDecoderMissing();
            }

            return false;
        }

        public static bool IsNoInterface(int hr) =>
            unchecked((uint)hr) == NoInterface;

        public static bool IsWrongThread(int hr) =>
            unchecked((uint)hr) == WrongThread;
    }

    /// <summary>
    /// Inbox JPEG XR CLSID. Prefer factory open; on packaged catalog
    /// miss, <c>CoCreate</c> / <c>DllGetClassObject</c> then
    /// <c>Initialize</c> from memory (<c>wmp</c>). Do not
    /// <c>CreateAsync(JpegXrDecoderId)</c> (E_NOINTERFACE; property
    /// missing on some WASDK).
    /// </summary>
    public static readonly Guid ClsidWmpDecoder = new("a26cec36-234c-4950-ae16-e34aace71d0d");

    public static readonly Guid IidWicBitmap = new("00000121-a8f2-4877-ba0a-fd2b6645fb94");

    public static readonly Guid IidBitmapDecoder = new("9edde9c7-3d7c-410a-ba78-0ebaf22aa18d");

    public static readonly Guid IidBitmapFrameDecode = new("3b16811b-6a43-4ec9-a813-3d930c13b940");

    public static readonly Guid IidStream = new("0000000c-0000-0000-c000-000000000046");

    public static readonly Guid IidClassFactory = new("00000001-0000-0000-c000-000000000046");

    public static readonly string[] SoftwareBitmapNativeMethods =
    [
        "GetIids",
        "GetRuntimeClassName",
        "GetTrustLevel",
        "GetData"
    ];

    /// <summary>
    /// Official <c>windows.graphics.imaging.interop.h</c>
    /// <c>IID_ISoftwareBitmapNative</c>. Do not use
    /// <c>94c952b4-…</c> (fake <c>E_NOINTERFACE</c>).
    /// </summary>
    public static readonly Guid IidSoftwareBitmapNative =
        new("94BC8415-04EA-4B2E-AF13-4DE95AA898EB");

    /// <summary>
    /// Flattened <c>IInspectable</c> + <c>GetData</c>. Do not inherit
    /// a C# Inspectable interface.
    /// </summary>
    [ComImport]
    [Guid("94BC8415-04EA-4B2E-AF13-4DE95AA898EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISoftwareBitmapNative
    {
        void GetIids();
        void GetRuntimeClassName();
        void GetTrustLevel();

        [PreserveSig]
        int GetData(ref Guid riid, out IntPtr ppv);
    }

#pragma warning disable CA1416 // Linked into Palace.Tests on Linux; callers are Windows-only.
    /// <summary>
    /// Typed RCW after a successful <c>QueryInterface</c>. Do
    /// <strong>not</strong> use this for a WIC IID QI'd from a CsWinRT
    /// object — <c>GetTypedObjectForIUnknown</c> returns the cached
    /// WinRT RCW and the cast fails. Use
    /// <see cref="TypedUniqueFromIUnknown{T}"/> instead.
    /// </summary>
    public static T? TypedFromIUnknown<T>(IntPtr unk) where T : class
    {
        if (unk == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return (T)Marshal.GetTypedObjectForIUnknown(unk, typeof(T));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Wrap a native WIC <c>ppv</c> without the RCW cache. Required after
    /// QI from <c>IWinRTObject.ThisPtr</c> so float <c>CopyPixels</c>
    /// keeps the WIC pointer instead of the CsWinRT
    /// <c>BitmapDecoder</c> wrapper.
    /// </summary>
    public static T? TypedUniqueFromIUnknown<T>(IntPtr unk) where T : class
    {
        if (unk == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return (T)Marshal.GetUniqueObjectForIUnknown(unk);
        }
        catch
        {
            return null;
        }
    }

    public static T? TypedFromUnknown<T>(object com) where T : class
    {
        var unk = Marshal.GetIUnknownForObject(com);
        try
        {
            return TypedFromIUnknown<T>(unk);
        }
        finally
        {
            Marshal.Release(unk);
        }
    }
#pragma warning restore CA1416

    public static readonly string[] ClassFactoryMethods = ["CreateInstance", "LockServer"];

    [ComImport]
    [Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICImagingFactory
    {
        [PreserveSig]
        int CreateDecoderFromFilename(
            [MarshalAs(UnmanagedType.LPWStr)] string wzFilename,
            IntPtr pguidVendor,
            uint dwDesiredAccess,
            uint metadataOptions,
            out IWICBitmapDecoder ppIDecoder);

        [PreserveSig]
        int CreateDecoderFromStream(
            [MarshalAs(UnmanagedType.Interface)] object pIStream,
            IntPtr pguidVendor,
            uint metadataOptions,
            out IWICBitmapDecoder ppIDecoder);

        [PreserveSig]
        int CreateDecoderFromFileHandle(
            UIntPtr hFile,
            IntPtr pguidVendor,
            uint metadataOptions,
            out IWICBitmapDecoder ppIDecoder);

        void CreateComponentInfo();

        [PreserveSig]
        int CreateDecoder(
            ref Guid guidContainerFormat,
            IntPtr pguidVendor,
            out IWICBitmapDecoder ppIDecoder);

        void CreateEncoder();
        void CreatePalette();
        void CreateFormatConverter(out IWICFormatConverter ppIFormatConverter);
        void CreateBitmapScaler(out IWICBitmapScaler ppIBitmapScaler);
        void CreateBitmapClipper();
        void CreateBitmapFlipRotator();
        void CreateStream(out IWICStream ppIWICStream);
    }

    [ComImport]
    [Guid("9edde9c7-3d7c-410a-ba78-0ebaf22aa18d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICBitmapDecoder
    {
        void QueryCapability();

        [PreserveSig]
        int Initialize(
            [MarshalAs(UnmanagedType.Interface)] object pIStream,
            uint cacheOptions);
        void GetContainerFormat();
        void GetDecoderInfo();
        void CopyPalette();
        void GetMetadataQueryReader();
        void GetPreview();
        void GetColorContexts();
        void GetThumbnail();
        void GetFrameCount();
        void GetFrame(uint index, out IWICBitmapFrameDecode ppIFrameDecode);
    }

    [ComImport]
    [Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICBitmapSource
    {
        void GetSize(out uint puiWidth, out uint puiHeight);
        void GetPixelFormat(out Guid pPixelFormat);
        void GetResolution(out double pDpiX, out double pDpiY);
        void CopyPalette();
        void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
    }

    /// <summary>
    /// Flattened <c>IWICBitmapSource</c> + <c>GetMetadataQueryReader</c>.
    /// Do not inherit the C# BitmapSource interface.
    /// </summary>
    [ComImport]
    [Guid("3b16811b-6a43-4ec9-a813-3d930c13b940")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICBitmapFrameDecode
    {
        void GetSize(out uint puiWidth, out uint puiHeight);
        void GetPixelFormat(out Guid pPixelFormat);
        void GetResolution(out double pDpiX, out double pDpiY);
        void CopyPalette();
        void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
        void GetMetadataQueryReader(out IWICMetadataQueryReader ppIMetadataQueryReader);
    }

    /// <summary>
    /// Flattened <c>IWICBitmapSource</c> + <c>Initialize</c>.
    /// </summary>
    [ComImport]
    [Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICFormatConverter
    {
        void GetSize(out uint puiWidth, out uint puiHeight);
        void GetPixelFormat(out Guid pPixelFormat);
        void GetResolution(out double pDpiX, out double pDpiY);
        void CopyPalette();
        void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
        void Initialize(
            IWICBitmapSource pISource,
            ref Guid dstFormat,
            uint dither,
            IntPtr pIPalette,
            double alphaThresholdPercent,
            uint paletteTranslate);
    }

    /// <summary>
    /// Flattened <c>IWICBitmapSource</c> + <c>Initialize</c>.
    /// </summary>
    [ComImport]
    [Guid("00000302-a8f2-4877-ba0a-fd2b6645fb94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICBitmapScaler
    {
        void GetSize(out uint puiWidth, out uint puiHeight);
        void GetPixelFormat(out Guid pPixelFormat);
        void GetResolution(out double pDpiX, out double pDpiY);
        void CopyPalette();
        void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
        void Initialize(
            IWICBitmapSource pISource,
            uint uiWidth,
            uint uiHeight,
            uint mode);
    }

    /// <summary>
    /// Flattened <c>IStream</c> + <c>InitializeFromMemory</c>. Do not
    /// inherit <c>ComTypes.IStream</c> (vtable would skip Read/Seek).
    /// </summary>
    [ComImport]
    [Guid("135ff860-22b7-4ddf-b0f6-218f4f299a43")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICStream
    {
        void Read(IntPtr pv, uint cb, IntPtr pcbRead);
        void Write(IntPtr pv, uint cb, IntPtr pcbWritten);
        void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition);
        void SetSize(long libNewSize);
        void CopyTo(IntPtr pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten);
        void Commit(uint grfCommitFlags);
        void Revert();
        void LockRegion(long libOffset, long cb, uint dwLockType);
        void UnlockRegion(long libOffset, long cb, uint dwLockType);
        void Stat(IntPtr pstatstg, uint grfStatFlag);
        void Clone(IntPtr ppstm);
        void InitializeFromIStream([MarshalAs(UnmanagedType.Interface)] object pIStream);
        void InitializeFromFilename(
            [MarshalAs(UnmanagedType.LPWStr)] string wzFileName,
            uint dwDesiredAccess);

        [PreserveSig]
        int InitializeFromMemory(IntPtr pbBuffer, uint cbBufferSize);
    }

    public static readonly string[] FactoryOpenMethods =
    [
        "CreateDecoderFromFilename",
        "CreateDecoderFromStream",
        "CreateDecoderFromFileHandle",
        "CreateComponentInfo",
        "CreateDecoder",
        "CreateEncoder",
        "CreatePalette",
        "CreateFormatConverter",
        "CreateBitmapScaler",
        "CreateBitmapClipper",
        "CreateBitmapFlipRotator",
        "CreateStream"
    ];

    public static readonly string[] StreamMethods =
    [
        "Read",
        "Write",
        "Seek",
        "SetSize",
        "CopyTo",
        "Commit",
        "Revert",
        "LockRegion",
        "UnlockRegion",
        "Stat",
        "Clone",
        "InitializeFromIStream",
        "InitializeFromFilename",
        "InitializeFromMemory"
    ];

    public static readonly Guid ContainerFormatWmp = new("57a37caa-367a-4540-916b-f183c1868a5f");

    [ComImport]
    [Guid("00000001-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer(int fLock);
    }

    [ComImport]
    [Guid("30989668-e1c9-4597-b395-458eedb808df")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICMetadataQueryReader
    {
        void GetContainerFormat();
        void GetLocation();
        void GetMetadataByName(
            [MarshalAs(UnmanagedType.LPWStr)] string wzName,
            ref PropVariant pvarValue);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public IntPtr data1;
        public IntPtr data2;
    }
}
