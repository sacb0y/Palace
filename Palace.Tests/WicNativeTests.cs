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
            ["filename", "handle", "memory", "wmp"],
            WicNative.WicDecoderOpen.Stages);
        Assert.Equal(
            "WIC decoder memory",
            WicNative.WicDecoderOpen.Failed(WicNative.WicDecoderOpen.Memory));
        Assert.Equal(
            "WIC decoder wmp 88982F50",
            WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.Wmp,
                unchecked((int)0x88982F50)));
        Assert.Equal(
            WicNative.ContainerFormatWmp,
            Guid.Parse("57a37caa-367a-4540-916b-f183c1868a5f"));
    }

    private static void AssertFlattened(Type type, string extra)
    {
        var methods = WicNative.DeclaredMethods(type);
        Assert.True(methods.Length > WicNative.BitmapSourceMethods.Length, type.Name);
        Assert.Equal(WicNative.BitmapSourceMethods, methods[..WicNative.BitmapSourceMethods.Length]);
        Assert.Equal(extra, methods[WicNative.BitmapSourceMethods.Length]);
    }
}
