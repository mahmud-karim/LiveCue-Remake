using System.Runtime.ExceptionServices;

namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// Runs test code on a single-threaded apartment thread: WPF resources and elements can only be created
/// there, and xUnit's own threads are not. A failure is carried back and thrown on the test thread.
/// </summary>
internal static class StaThread
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // Carried to the test thread and rethrown there.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    public static T Run<T>(Func<T> function)
    {
        T result = default!;
        Run(() => { result = function(); });
        return result;
    }
}
