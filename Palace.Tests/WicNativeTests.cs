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
    public void DecoderGetFrame_UsesVtableSlotPastIUnknown()
    {
        // IUnknown(3) + QueryCapability…GetFrameCount(10) → GetFrame at 13.
        Assert.Equal(13, WicNative.DecoderGetFrameVtableSlot);
    }

    [Fact]
    public void CreateDecoderFromFilename_OutIsIntPtrNotManagedDecoder()
    {
        var method = typeof(WicNative.IWICImagingFactory).GetMethod("CreateDecoderFromFilename");
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        var outParam = parameters[^1];
        Assert.Equal(typeof(IntPtr).MakeByRefType(), outParam.ParameterType);
    }

    private static void AssertFlattened(Type type, string extra)
    {
        var methods = WicNative.DeclaredMethods(type);
        Assert.True(methods.Length > WicNative.BitmapSourceMethods.Length, type.Name);
        Assert.Equal(WicNative.BitmapSourceMethods, methods[..WicNative.BitmapSourceMethods.Length]);
        Assert.Equal(extra, methods[WicNative.BitmapSourceMethods.Length]);
    }
}
