using System.Windows.Threading;

namespace LiveCue.Desktop.Infrastructure;

/// <summary>
/// Moves work onto the UI thread. Relay events arrive on request threads; view models post their
/// reaction through this so they only ever change state on the thread that owns them.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> on the UI thread. Actions run in the order posted.</summary>
    void Post(Action action);
}

/// <summary>The real dispatcher of the WPF application.</summary>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) => dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
}
