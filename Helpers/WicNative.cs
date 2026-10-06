using System.Reflection;
using System.Runtime.InteropServices;

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
    /// Packaged WinUI: the WIC component catalog omits HD Photo /
    /// <c>GUID_ContainerFormatWmp</c> (<c>WINCODEC_ERR_COMPONENTNOTFOUND</c>
    /// <c>0x88982F50</c>). Do not <c>CreateDecoder(Wmp)</c>. CoCreate
    /// inbox <c>CLSID_WICWmpDecoder</c> (or <c>DllGetClassObject</c> on
    /// <c>WindowsCodecs.dll</c>) and <c>Initialize</c> a pinned stream.
    /// </summary>
    public static class WicDecoderOpen
    {
        public const string Filename = "filename";
        public const string Handle = "handle";
        public const string Memory = "memory";
        public const string Clsid = "clsid";
        public const uint ComponentNotFound = 0x88982F50;

        public static readonly string[] Stages = [Filename, Handle, Memory, Clsid];

        public static string Failed(string stage, int hr = 0)
        {
            var suffix = hr == 0 ? "" : " " + unchecked((uint)hr).ToString("X8");
            return "WIC decoder " + stage + suffix;
        }

        public static bool IsComponentNotFound(int hr) =>
            unchecked((uint)hr) == ComponentNotFound;
    }

    /// <summary>
    /// Inbox JPEG XR / HD Photo decoder in <c>WindowsCodecs.dll</c>.
    /// Same CLSID as WinRT <c>BitmapDecoder.JpegXrDecoderId</c>.
    /// </summary>
    public static readonly Guid ClsidWmpDecoder = new("a26cec36-234c-4950-ae16-e34aace71d0d");

    public static readonly Guid IidBitmapDecoder = new("9edde9c7-3d7c-410a-ba78-0ebaf22aa18d");

    public static readonly Guid IidClassFactory = new("00000001-0000-0000-c000-000000000046");

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
