using System.Windows.Threading;

namespace LiveCue.Desktop.Infrastructure;

/// <summary>
/// The real UI thread of the WPF application. Relay events arrive on thread-pool threads; the session
/// posts its reaction through this, so view-model state only ever changes on the thread that owns it.
/// </summary>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action) => dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
}
