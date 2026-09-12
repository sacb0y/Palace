using System.IO;
using Palace.Helpers;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

public sealed class CloudReadGuardTests
{
    private static readonly byte[] Png1x1 =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE,
        0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54,
        0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00,
        0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE, 0xD4, 0xEF,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    [Fact]
    public void ImageDimensions_ReadsLocalPng()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-dim-local-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, Png1x1);
        try
        {
            var size = ImageDimensions.TryRead(path);
            Assert.Equal((1, 1), size);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImageDimensions_RefusesOnlineOnly_WithoutOpening()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-dim-online-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, Png1x1);
        try
        {
            if (!CloudFileTests.TryStampOnlineOnly(path))
            {
                return;
            }

            var before = new FileInfo(path).Length;
            Assert.Null(ImageDimensions.TryRead(path));
            Assert.Equal(before, new FileInfo(path).Length);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HashFile_HashesLocalFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-hash-local-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            var hash = await HashService.HashFileAsync(path, 4);
            Assert.False(HashService.IsCloudStub(hash));
            Assert.Equal(64, hash.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PrefixHash_NeedsFullRecallOnlyWhenOnlineOnlyAndLarge()
    {
        Assert.True(HashService.UsesPrefixHash(HashService.PrefixHashThresholdBytes + 1));
        Assert.False(HashService.UsesPrefixHash(HashService.PrefixHashThresholdBytes));
        Assert.True(HashService.NeedsFullRecall(true, HashService.PrefixHashThresholdBytes + 1));
        Assert.False(HashService.NeedsFullRecall(false, HashService.PrefixHashThresholdBytes + 1));
        Assert.False(HashService.NeedsFullRecall(true, 1024));
        Assert.True(HashService.ApplyExtractedMetadata(false));
        Assert.False(HashService.ApplyExtractedMetadata(true));
    }

    [Fact]
    public async Task RecallFully_ReadsEntireFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-recall-" + Guid.NewGuid().ToString("N") + ".bin");
        var payload = new byte[12 * 1024];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(path, payload);
        try
        {
            await HashService.RecallFullyAsync(path);
            Assert.Equal(payload.Length, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CloudStubHash_IsStableAndPrefixed()
    {
        var when = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var a = HashService.CloudStubHash(@"D:\cloud\a.jpg", 10, when);
        var b = HashService.CloudStubHash(@"D:\cloud\a.jpg", 10, when);
        var c = HashService.CloudStubHash(@"D:\cloud\a.jpg", 11, when);
        Assert.True(HashService.IsCloudStub(a));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ScanContent_RefusesDropboxPlaceholderAndBareOffline()
    {
        var dropbox = FileAttributes.Archive | FileAttributes.SparseFile | FileAttributes.ReparsePoint | FileAttributes.Offline;
        Assert.False(ScanContent.MayReadOriginal(dropbox));
        Assert.False(ScanContent.MayGenerateScanThumbnail(dropbox));
        Assert.False(ScanContent.MayReadOriginal(FileAttributes.Offline));
        Assert.True(ScanContent.MayReadOriginal(FileAttributes.Archive));
        Assert.True(ScanContent.MayReadOriginal(
            FileAttributes.Archive | FileAttributes.ReparsePoint | FileAttributes.Offline));
        Assert.True(ScanContent.MayGenerateScanThumbnail(
            FileAttributes.Archive | FileAttributes.ReparsePoint | CloudFile.Pinned | FileAttributes.Offline));
        Assert.False(ScanContent.MayReadOriginal((string?)null));
        Assert.False(ScanContent.MayGenerateScanThumbnail(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }
}
