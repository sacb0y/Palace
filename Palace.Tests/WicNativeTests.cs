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
            ["filename", "handle", "memory", "winrt"],
            WicNative.WicDecoderOpen.Stages);
        Assert.Equal(
            "WIC decoder memory",
            WicNative.WicDecoderOpen.Failed(WicNative.WicDecoderOpen.Memory));
        Assert.Equal(
            "WIC decoder winrt 80004002",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.Winrt,
                unchecked((int)0x80004002)));
        Assert.True(
            WicNative.WicDecoderOpen.IsComponentNotFound(unchecked((int)0x88982F50)));
        Assert.True(
            WicNative.WicDecoderOpen.IsNoInterface(unchecked((int)0x80004002)));
        Assert.False(WicNative.WicDecoderOpen.IsComponentNotFound(0));
    }

    [Fact]
    public void JpegXrDecoderId_MatchesWinrtInboxId()
    {
        Assert.Equal(
            Guid.Parse("a26cec36-234c-4950-ae16-e34aace71d0d"),
            WicNative.JpegXrDecoderId);
        Assert.Equal(WicNative.ClsidWmpDecoder, WicNative.JpegXrDecoderId);
        Assert.Equal(
            Guid.Parse("3b16811b-6a43-4ec9-a813-3d930c13b940"),
            WicNative.IidBitmapFrameDecode);
        Assert.Equal(
            Guid.Parse("0000000c-0000-0000-c000-000000000046"),
            WicNative.IidStream);
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
