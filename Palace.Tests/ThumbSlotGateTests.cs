using System.Text;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public class ThumbSlotGateTests
{
    [Fact]
    public async Task AbandonedHolder_FreesTheSlotWhileTheDecodeIsStillRunning()
    {
        var gate = new ThumbSlotGate(1);
        var dir = Path.Combine(Path.GetTempPath(), "palace-slot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "still.hdr");
        File.WriteAllBytes(path, RadianceBytes());
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDecode = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ThumbSlot? slot = null;
        var running = Task.Run(async () =>
        {
            slot = await gate.EnterAsync(cts.Token).ConfigureAwait(false);
            try
            {
                entered.TrySetResult();
                await releaseDecode.Task.ConfigureAwait(false);
                return StillDecode.TryDecodeFile(path, 32, cts.Token);
            }
            finally
            {
                slot.Dispose();
            }
        });

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, gate.Available);
        Assert.True(slot!.IsCurrent);

        cts.Cancel();
        Assert.Equal(1, gate.Available);
        Assert.False(slot.IsCurrent);

        var next = await gate.EnterAsync(CancellationToken.None);
        Assert.Equal(0, gate.Available);

        releaseDecode.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
        next.Dispose();
        Assert.Equal(1, gate.Available);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void RadianceAndScrgb_StopWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = new MemoryStream(RadianceBytes());
        Assert.Throws<OperationCanceledException>(() => RadianceFile.TryDecode(stream, 1, 1, cts.Token));

        var rgba = new float[8 * 8 * 4];
        Assert.Throws<OperationCanceledException>(() => HdrPixels.BoxScaleScrgb(rgba, 8, 8, 2, 2, cts.Token));
        Assert.Throws<OperationCanceledException>(() => StillDecode.FromScrgb(rgba, 8, 8, 4, cts.Token));
        Assert.Throws<OperationCanceledException>(() => ToneMap.ScrgbToBgra8(rgba, 8, 8, cts.Token));
        Assert.Throws<OperationCanceledException>(() => ExrFile.TryDecode([], cts.Token));
    }

    private static byte[] RadianceBytes()
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 1\n"));
        ms.Write([128, 128, 128, 128]);
        return ms.ToArray();
    }
}
