using System.Collections.Concurrent;

namespace VideoAnalysis.App.Tests;

/// <summary>Runs work on a single dedicated STA thread, since WPF elements (like
/// <c>SlitScanControlPanel</c>) are thread-affine and require an STA apartment - xUnit's own test
/// threads are MTA. All tests that touch WPF objects share one thread (via
/// <see cref="SlitScanPanelFixture"/>) both to satisfy that affinity and to avoid the
/// one-Application-per-process restriction that a thread-per-test approach would hit.</summary>
public sealed class StaThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaThread()
    {
        _thread = new Thread(() =>
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                action();
            }
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
    }

    public T Invoke<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);
        _queue.Add(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        return result;
    }

    public void Invoke(Action action) => Invoke<object?>(() =>
    {
        action();
        return null;
    });

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
    }
}
