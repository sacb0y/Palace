using System.IO;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class CloudFileTests
{
    [Fact]
    public void IsOnlineOnly_NormalFile_IsFalse()
    {
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.Normal));
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.Archive));
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.ReadOnly | FileAttributes.Archive));
    }

    [Fact]
    public void IsOnlineOnly_RecallOnDataAccess_IsTrue()
    {
        Assert.True(CloudFile.IsOnlineOnly(CloudFile.RecallOnDataAccess));
        Assert.True(CloudFile.IsOnlineOnly((FileAttributes)0x00400000));
        Assert.True(CloudFile.IsOnlineOnly(FileAttributes.Archive | CloudFile.RecallOnDataAccess));
    }

    [Fact]
    public void IsOnlineOnly_RecallOnOpen_IsTrue()
    {
        Assert.True(CloudFile.IsOnlineOnly(CloudFile.RecallOnOpen));
        Assert.True(CloudFile.IsOnlineOnly((FileAttributes)0x00040000));
    }

    [Fact]
    public void IsOnlineOnly_Offline_IsTrue()
    {
        Assert.True(CloudFile.IsOnlineOnly(FileAttributes.Offline));
        Assert.True(CloudFile.IsOnlineOnly(CloudFile.Offline));
    }

    [Fact]
    public void IsOnlineOnly_MissingPath_IsFalse()
    {
        Assert.False(CloudFile.IsOnlineOnly((string?)null));
        Assert.False(CloudFile.IsOnlineOnly(""));
        Assert.False(CloudFile.IsOnlineOnly(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin")));
    }

    [Fact]
    public void IsOnlineOnly_LocalTempFile_IsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-cloudfile-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "local");
        try
        {
            Assert.False(CloudFile.IsOnlineOnly(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StampedOfflineFile_IsOnlineOnly_AndScanSafeReadsDoNotChangeSize()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-onlineonly-" + Guid.NewGuid().ToString("N") + ".bin");
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(path, payload);
        try
        {
            if (!TryStampOnlineOnly(path))
            {
                return;
            }

            var before = new FileInfo(path).Length;
            Assert.True(CloudFile.IsOnlineOnly(path));
            _ = File.GetAttributes(path);
            var after = new FileInfo(path).Length;
            Assert.Equal(before, after);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    private static bool TryStampOnlineOnly(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);
            return CloudFile.IsOnlineOnly(path);
        }
        catch
        {
            return false;
        }
    }
}
