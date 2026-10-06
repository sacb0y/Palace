using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class LoadGateTests
{
    [Fact]
    public void Begin_SetsIsLoading_DisposeClearsEvenAfterThrow()
    {
        var gate = new LoadGate();
        Assert.False(gate.IsLoading);

        try
        {
            using (gate.Begin())
            {
                Assert.True(gate.IsLoading);
                throw new InvalidOperationException("load failed");
            }
        }
        catch (InvalidOperationException)
        {
            // Expected — mirrors ErrorReporter.RunAsync swallowing after the action throws.
        }

        Assert.False(gate.IsLoading);
    }

    [Fact]
    public async Task Begin_ClearsAfterAwaitedFailure()
    {
        var gate = new LoadGate();

        try
        {
            using (gate.Begin())
            {
                await Task.Yield();
                throw new IOException("catalog down");
            }
        }
        catch (IOException)
        {
        }

        Assert.False(gate.IsLoading);
    }
}
