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

    [ComImport]
    [Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICImagingFactory
    {
        void CreateDecoderFromFilename(
            [MarshalAs(UnmanagedType.LPWStr)] string wzFilename,
            IntPtr pguidVendor,
            uint dwDesiredAccess,
            uint metadataOptions,
            out IWICBitmapDecoder ppIDecoder);

        void CreateDecoderFromStream();
        void CreateDecoderFromFileHandle();
        void CreateComponentInfo();
        void CreateDecoder();
        void CreateEncoder();
        void CreatePalette();
        void CreateFormatConverter(out IWICFormatConverter ppIFormatConverter);
        void CreateBitmapScaler(out IWICBitmapScaler ppIBitmapScaler);
    }

    [ComImport]
    [Guid("9edde9c7-3d7c-410a-ba78-0ebaf22aa18d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWICBitmapDecoder
    {
        void QueryCapability();
        void Initialize();
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
