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
    public void IsOnlineOnly_PinnedWithoutRecall_IsLocal()
    {
        Assert.False(CloudFile.IsOnlineOnly(CloudFile.Pinned));
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.Archive | CloudFile.Pinned | CloudFile.Offline));
        Assert.False(CloudFile.IsOnlineOnly(
            FileAttributes.ReparsePoint | CloudFile.Pinned | CloudFile.Offline));
    }

    [Fact]
    public void IsOnlineOnly_PinnedWithRecall_IsTrue()
    {
        Assert.True(CloudFile.IsOnlineOnly(CloudFile.Pinned | CloudFile.RecallOnDataAccess));
        Assert.True(CloudFile.IsOnlineOnly(CloudFile.Pinned | CloudFile.RecallOnOpen));
    }

    [Fact]
    public void IsOnlineOnly_HydratedReparsePointWithLeftoverOffline_IsLocal()
    {
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.Archive | FileAttributes.ReparsePoint));
        Assert.False(CloudFile.IsOnlineOnly(
            FileAttributes.Archive | FileAttributes.ReparsePoint | CloudFile.Offline));
        Assert.False(CloudFile.IsOnlineOnly(
            FileAttributes.Archive | FileAttributes.ReparsePoint | CloudFile.Unpinned));
    }

    [Fact]
    public void IsOnlineOnly_DropboxSparseReparsePlaceholder_IsTrue()
    {
        // Dropbox Files On-Demand omits RecallOnDataAccess; Explorer uses Sparse + Reparse.
        Assert.True(CloudFile.IsOnlineOnly(
            FileAttributes.Archive | FileAttributes.SparseFile | FileAttributes.ReparsePoint));
        Assert.True(CloudFile.IsOnlineOnly(
            FileAttributes.Archive | FileAttributes.SparseFile | FileAttributes.ReparsePoint | CloudFile.Offline));
        Assert.True(CloudFile.IsOnlineOnly(
            FileAttributes.SparseFile | FileAttributes.ReparsePoint | CloudFile.Unpinned | CloudFile.Offline));
        Assert.False(ScanContent.MayReadOriginal(
            FileAttributes.Archive | FileAttributes.SparseFile | FileAttributes.ReparsePoint | CloudFile.Offline));
        Assert.False(ScanContent.MayGenerateScanThumbnail(
            FileAttributes.SparseFile | FileAttributes.ReparsePoint));
    }

    [Fact]
    public void IsOnlineOnly_SparseWithoutReparse_IsLocal()
    {
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.Archive | FileAttributes.SparseFile));
        Assert.True(ScanContent.MayReadOriginal(FileAttributes.Archive | FileAttributes.SparseFile));
    }

    [Fact]
    public void IsCloudBacked_OnDemandAndPinned_IsTrue()
    {
        Assert.False(CloudFile.IsCloudBacked(FileAttributes.Normal));
        Assert.False(CloudFile.IsCloudBacked(FileAttributes.Archive));
        Assert.False(CloudFile.IsCloudBacked(FileAttributes.Directory | CloudFile.Offline));
        Assert.True(CloudFile.IsCloudBacked(CloudFile.RecallOnDataAccess));
        Assert.True(CloudFile.IsCloudBacked(CloudFile.Pinned));
        Assert.True(CloudFile.IsCloudBacked(CloudFile.Unpinned));
        Assert.True(CloudFile.IsCloudBacked(CloudFile.Offline));
        Assert.True(CloudFile.IsCloudBacked(FileAttributes.ReparsePoint));
        Assert.True(CloudFile.IsCloudBacked(FileAttributes.SparseFile | FileAttributes.ReparsePoint));
        Assert.True(CloudFile.IsCloudBacked(
            FileAttributes.Archive | FileAttributes.ReparsePoint | CloudFile.Offline));
    }

    [Fact]
    public void IsOnlineOnly_ReparsePointWithRecall_IsTrue()
    {
        Assert.True(CloudFile.IsOnlineOnly(
            FileAttributes.Archive | FileAttributes.ReparsePoint | CloudFile.RecallOnDataAccess));
        Assert.True(CloudFile.IsOnlineOnly(
            FileAttributes.ReparsePoint | CloudFile.Offline | CloudFile.RecallOnDataAccess));
        Assert.True(CloudFile.IsOnlineOnly(
            FileAttributes.ReparsePoint | CloudFile.Unpinned | CloudFile.RecallOnOpen));
    }

    [Fact]
    public void IsOnlineOnly_Directory_IsFalse()
    {
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.Directory));
        Assert.False(CloudFile.IsOnlineOnly(FileAttributes.Directory | CloudFile.Offline));
    }

    [Fact]
    public void IsOnlineOnly_MissingPath_IsFalse()
    {
        Assert.False(CloudFile.IsOnlineOnly((string?)null));
        Assert.False(CloudFile.IsOnlineOnly(""));
        Assert.False(CloudFile.IsOnlineOnly(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin")));
        Assert.False(CloudFile.Exists((string?)null));
        Assert.False(CloudFile.Exists(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin")));
    }

    [Fact]
    public void Exists_LocalTempFile_IsTrue_AndNotOnlineOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-cloudfile-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "local");
        try
        {
            Assert.True(CloudFile.Exists(path));
            Assert.True(CloudFile.TryGetAttributes(path, out var attrs));
            Assert.False(CloudFile.IsOnlineOnly(path));
            Assert.False(CloudFile.IsOnlineOnly(attrs));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Exists_Directory_IsFalse()
    {
        Assert.False(CloudFile.Exists(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
        Assert.False(CloudFile.TryGetAttributes(Path.GetTempPath(), out _));
    }

    [Fact]
    public void FilterLocalPaths_SkipsOnlineOnly_KeepsLocal()
    {
        var local = Path.Combine(Path.GetTempPath(), "palace-copy-local-" + Guid.NewGuid().ToString("N") + ".bin");
        var online = Path.Combine(Path.GetTempPath(), "palace-copy-online-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(local, [1, 2, 3]);
        File.WriteAllBytes(online, [4, 5, 6]);
        try
        {
            if (!TryStampOnlineOnly(online))
            {
                return;
            }

            var kept = CloudFile.FilterLocalPaths([local, online], out var skipped);
            Assert.Equal(1, skipped);
            Assert.Equal([local], kept);
        }
        finally
        {
            File.SetAttributes(online, FileAttributes.Normal);
            File.Delete(local);
            File.Delete(online);
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
            Assert.True(CloudFile.Exists(path));
            Assert.True(CloudFile.IsOnlineOnly(path));
            Assert.True(CloudFile.TryGetAttributes(path, out var attrs));
            Assert.True(CloudFile.IsOnlineOnly(attrs));
            var after = new FileInfo(path).Length;
            Assert.Equal(before, after);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    internal static bool TryStampOnlineOnly(string path)
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
