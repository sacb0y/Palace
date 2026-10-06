using System.Runtime.InteropServices.ComTypes;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class WicNativeTests
{
    [Fact]
    public void DerivedWicInterfaces_FlattenBitmapSourceVtable()
    {
        Assert.Equal(
            WicNative.BitmapSourceMethods,
            WicNative.DeclaredMethods(typeof(WicNative.IWICBitmapSource)));

        AssertFlattened(typeof(WicNative.IWICBitmapScaler), "Initialize");
        AssertFlattened(typeof(WicNative.IWICFormatConverter), "Initialize");
        AssertFlattened(typeof(WicNative.IWICBitmapFrameDecode), "GetMetadataQueryReader");
    }

    [Fact]
    public void DerivedWicInterfaces_DoNotInheritBitmapSource()
    {
        Assert.DoesNotContain(
            typeof(WicNative.IWICBitmapSource),
            typeof(WicNative.IWICBitmapScaler).GetInterfaces());
        Assert.DoesNotContain(
            typeof(WicNative.IWICBitmapSource),
            typeof(WicNative.IWICFormatConverter).GetInterfaces());
        Assert.DoesNotContain(
            typeof(WicNative.IWICBitmapSource),
            typeof(WicNative.IWICBitmapFrameDecode).GetInterfaces());
    }

    [Fact]
    public void FactoryOpenMethods_MatchDeclaredVtable()
    {
        Assert.Equal(
            WicNative.FactoryOpenMethods,
            WicNative.DeclaredMethods(typeof(WicNative.IWICImagingFactory)));
        Assert.Equal(
            "CreateDecoderFromFileHandle",
            WicNative.DeclaredMethods(typeof(WicNative.IWICImagingFactory))[2]);
        Assert.Equal(
            "CreateDecoder",
            WicNative.DeclaredMethods(typeof(WicNative.IWICImagingFactory))[4]);
        Assert.Equal(
            "CreateStream",
            WicNative.DeclaredMethods(typeof(WicNative.IWICImagingFactory))[^1]);
    }

    [Fact]
    public void StreamMethods_FlattenIStreamBeforeInitializeFromMemory()
    {
        Assert.Equal(
            WicNative.StreamMethods,
            WicNative.DeclaredMethods(typeof(WicNative.IWICStream)));
        Assert.DoesNotContain(
            typeof(IStream),
            typeof(WicNative.IWICStream).GetInterfaces());
        Assert.Equal(
            "InitializeFromMemory",
            WicNative.DeclaredMethods(typeof(WicNative.IWICStream))[^1]);
    }

    [Fact]
    public void DecoderInitialize_IsSecondVtableSlot()
    {
        Assert.Equal(
            "Initialize",
            WicNative.DeclaredMethods(typeof(WicNative.IWICBitmapDecoder))[1]);
    }

    [Fact]
    public void WicDecoderOpen_FailedIncludesStageAndHRESULT()
    {
        Assert.Equal(
            [
                "filename",
                "handle",
                "memory",
                "CreateAsync",
                "qi",
                "ras",
                "GetSoftwareBitmap",
                "LockBuffer",
                "GetPixelData",
                "unorm"
            ],
            WicNative.WicDecoderOpen.Stages);
        Assert.Equal(
            [
                "CreateAsync",
                "qi",
                "ras",
                "GetSoftwareBitmap",
                "LockBuffer",
                "GetPixelData",
                "unorm"
            ],
            WicNative.WicDecoderOpen.WinrtCalls);
        Assert.Equal(
            "WIC decoder memory",
            WicNative.WicDecoderOpen.Failed(WicNative.WicDecoderOpen.Memory));
        Assert.Equal(
            "WIC decoder GetSoftwareBitmap 80004002",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.GetSoftwareBitmap,
                unchecked((int)0x80004002)));
        Assert.Equal(
            "WIC decoder CreateAsync 80004002",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.CreateAsync,
                unchecked((int)0x80004002)));
        Assert.Equal(
            "WIC decoder LockBuffer 80004002",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.LockBuffer,
                unchecked((int)0x80004002)));
        Assert.Equal(
            "WIC decoder GetPixelData 8001010E",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.GetPixelData,
                unchecked((int)0x8001010E)));
        Assert.Equal(
            "WIC decoder unorm",
            WicNative.WicDecoderOpen.Failed(WicNative.WicDecoderOpen.UnormClamp));
        Assert.Equal(
            "WIC decoder qi 80004002",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.Qi,
                unchecked((int)0x80004002)));
        Assert.Equal(
            "WIC decoder ras 88982F50",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.Ras,
                unchecked((int)0x88982F50)));
        Assert.Equal(
            Guid.Parse("94BC8415-04EA-4B2E-AF13-4DE95AA898EB"),
            WicNative.IidSoftwareBitmapNative);
        Assert.True(
            WicNative.WicDecoderOpen.IsComponentNotFound(unchecked((int)0x88982F50)));
        Assert.True(
            WicNative.WicDecoderOpen.IsNoInterface(unchecked((int)0x80004002)));
        Assert.True(
            WicNative.WicDecoderOpen.IsWrongThread(unchecked((int)0x8001010E)));
        Assert.False(WicNative.WicDecoderOpen.IsWrongThread(unchecked((int)0x80004002)));
        Assert.False(WicNative.WicDecoderOpen.IsComponentNotFound(0));
        var stamped = new InvalidOperationException();
        stamped.HResult = unchecked((int)0x80004002);
        Assert.Equal(
            "WIC decoder GetSoftwareBitmap 80004002",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.GetSoftwareBitmap,
                stamped));
    }

    [Fact]
    public void SoftwareBitmapNative_FlattensInspectableBeforeGetData()
    {
        Assert.Equal(
            WicNative.SoftwareBitmapNativeMethods,
            WicNative.DeclaredMethods(typeof(WicNative.ISoftwareBitmapNative)));
        Assert.Equal(
            "GetData",
            WicNative.DeclaredMethods(typeof(WicNative.ISoftwareBitmapNative))[^1]);
        Assert.Equal(
            Guid.Parse("00000121-a8f2-4877-ba0a-fd2b6645fb94"),
            WicNative.IidWicBitmap);
        Assert.Null(WicNative.TypedFromIUnknown<WicNative.IWICBitmapDecoder>(IntPtr.Zero));
        Assert.Equal(
            WicNative.ClassFactoryMethods,
            WicNative.DeclaredMethods(typeof(WicNative.IClassFactory)));
    }

    private static void AssertFlattened(Type type, string extra)
    {
        var methods = WicNative.DeclaredMethods(type);
        Assert.True(methods.Length > WicNative.BitmapSourceMethods.Length, type.Name);
        Assert.Equal(WicNative.BitmapSourceMethods, methods[..WicNative.BitmapSourceMethods.Length]);
        Assert.Equal(extra, methods[WicNative.BitmapSourceMethods.Length]);
    }
}
