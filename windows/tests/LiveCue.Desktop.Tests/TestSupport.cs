using System.Runtime.ExceptionServices;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.SelfTest;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Tests;

/// <summary>Runs posted work at once, on the calling thread: the "UI thread" of a view-model test.</summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>
/// The app's composition root with a scripted relay, in-memory settings and a hand-moved clock: the
/// same wiring the self-test uses, without a window.
/// </summary>
internal sealed class ScriptedApp : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 14, 2, TimeSpan.Zero);

    public ScriptedApp(HostSettings? settings = null)
    {
        Clock = new ManualClock(Start);
        Script = new RelayScript(Clock);
        Services = AppServices.Create(
            new ImmediateDispatcher(),
            new InMemorySettingsStore(settings),
            Path.Combine(Path.GetTempPath(), "LiveCue-desktop-tests-not-used"),
            Script.Create,
            Clock);
    }

    public ManualClock Clock { get; }

    public RelayScript Script { get; }

    public AppServices Services { get; }

    /// <summary>Waits until every event the script raised has reached the view models.</summary>
    public Task DeliverAsync() => Services.Supervisor.WhenIdleAsync();

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}

/// <summary>WPF resources can only be loaded on a single-threaded apartment thread; xUnit's are not.</summary>
internal static class Sta
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
}
