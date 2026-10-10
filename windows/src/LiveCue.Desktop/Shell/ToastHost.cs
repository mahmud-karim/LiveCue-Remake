using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveCue.Desktop.Shell;

/// <summary>
/// One toast at a time (design system 4.11): a new one replaces the current one, and it leaves by
/// itself after <see cref="Duration"/>. The timer comes from the clock, so tests can advance it.
/// </summary>
internal sealed class ToastHost : ObservableObject, IToastHost, IDisposable
{
    /// <summary>How long a toast stays (W-design 5.9).</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);

    private readonly IUiDispatcher _ui;
    private readonly ITimer _timer;
    private string? _text;

    public ToastHost(IUiDispatcher ui, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(clock);
        _ui = ui;
        _timer = clock.CreateTimer(_ => _ui.Post(() => Text = null), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The toast on screen, or null.</summary>
    public string? Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public void Show(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Text = text;
        _timer.Change(Duration, Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => _timer.Dispose();
}
