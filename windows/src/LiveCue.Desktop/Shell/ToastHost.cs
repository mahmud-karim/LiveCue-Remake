using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveCue.Desktop.Shell;

/// <summary>
/// One toast at a time (design system 4.11): a new one replaces the current one, and it leaves by
/// itself after <see cref="Duration"/>. While the pointer rests on it the countdown is held, and it
/// starts again when the pointer leaves. The timer comes from the clock, so tests can advance it.
/// </summary>
internal sealed class ToastHost : ObservableObject, IToastHost, IDisposable
{
    /// <summary>How long a toast stays (W-design 5.9).</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);

    private readonly IUiDispatcher _ui;
    private readonly ITimer _timer;
    private string? _text;
    private bool _held;

    public ToastHost(IUiDispatcher ui, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(clock);
        _ui = ui;
        _timer = clock.CreateTimer(_ => _ui.Post(Expire), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
        if (!_held)
        {
            _timer.Change(Duration, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>The pointer is on the toast: it stays.</summary>
    public void Hold()
    {
        _held = true;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The pointer left: the toast stays for its full time again.</summary>
    public void Release()
    {
        _held = false;
        if (Text is not null)
        {
            _timer.Change(Duration, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose() => _timer.Dispose();

    private void Expire()
    {
        if (!_held)
        {
            Text = null;
        }
    }
}
