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
    /// Run a WinRT await on the UI dispatcher. Packaged
    /// <c>GetSoftwareBitmapAsync</c> can throw
    /// <c>RPC_E_WRONG_THREAD</c> on an MTA pool thread.
    /// </summary>
    public static Task<T> RunTaskAsync<T>(Func<Task<T>> work)
    {
        var dq = App.DispatcherQueue;
        if (dq is null || dq.HasThreadAccess || App.Window is null)
        {
            return work();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(DispatcherQueuePriority.Normal, () => Complete(work, tcs)))
        {
            return work();
        }

        return tcs.Task;
    }

    private static void Complete<T>(Func<Task<T>> work, TaskCompletionSource<T> tcs)
    {
        try
        {
            var task = work();
            if (task.IsCompleted)
            {
                Finish(task, tcs);
                return;
            }

            task.ContinueWith(
                static (t, state) => Finish(t, (TaskCompletionSource<T>)state!),
                tcs,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    private static void Finish<T>(Task<T> task, TaskCompletionSource<T> tcs)
    {
        if (task.IsFaulted)
        {
            tcs.TrySetException(task.Exception!.InnerExceptions);
        }
        else if (task.IsCanceled)
        {
            tcs.TrySetCanceled();
        }
        else
        {
            tcs.TrySetResult(task.Result);
        }
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
