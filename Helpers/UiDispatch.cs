using Microsoft.UI.Dispatching;

namespace Palace.Helpers;

internal static class UiDispatch
{
    public static Task RunAsync(Action action)
    {
        var dq = App.DispatcherQueue;
        if (dq is null || dq.HasThreadAccess || App.Window is null)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            action();
            return Task.CompletedTask;
        }

        return tcs.Task;
    }

    /// <summary>
    /// Yields to the dispatcher so layout and input can run between mosaic chunks.
    /// </summary>
    public static Task YieldAsync()
    {
        var dq = App.DispatcherQueue;
        if (dq is null || App.Window is null)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(DispatcherQueuePriority.Low, () => tcs.SetResult()))
        {
            return Task.CompletedTask;
        }

        return tcs.Task;
    }
}
