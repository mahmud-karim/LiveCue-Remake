using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.Live;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.Session;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Shell;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Composition;

/// <summary>
/// The composition root: the session, the view models and the three small shell objects for one run of
/// the app, over one <see cref="HostingServices"/>. The normal start-up and the self-test both come
/// through here, so the self-test exercises the real supervisor, the real session and the real view
/// models, with scripted parts where the outside world would be.
/// </summary>
internal sealed class AppServices : IAsyncDisposable
{
    private readonly DesktopSession _session;
    private readonly ToastHost _toasts;

    private AppServices(HostingServices hosting, DesktopSession session, ShellViewModel shell, ToastHost toasts)
    {
        Hosting = hosting;
        _session = session;
        Shell = shell;
        _toasts = toasts;
    }

    public HostingServices Hosting { get; }

    public IDesktopSession Session => _session;

    public ShellViewModel Shell { get; }

    /// <summary>The real application: the data folder of architecture 3.5 and the real relay.</summary>
    public static AppServices CreateForUser(IUiDispatcher ui) => Create(
        HostingFactory.Create(new SupervisorOptions(DataFolder.Resolve(), AppEnvironment.Version)),
        ui,
        TimeProvider.System,
        new WpfClipboard(),
        new ShellLauncher());

    /// <summary>Wires the session and the view models to the given hosting services.</summary>
    public static AppServices Create(
        HostingServices hosting,
        IUiDispatcher ui,
        TimeProvider clock,
        IClipboard clipboard,
        IExternalLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(hosting);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(launcher);

        var navigator = new Navigator();
        var dialogs = new DialogHost();
        var toasts = new ToastHost(ui, clock);
        var session = new DesktopSession(hosting.Supervisor, hosting.Mappings, hosting.Endpoints, ui, clock);

        var live = new LiveViewModel(session, navigator, dialogs, clipboard, toasts, clock, ui);
        var activity = new ActivityViewModel(session, clock);
        var pairing = new PairingViewModel(session, navigator, dialogs, clipboard, toasts, clock, ui);
        var settings = new SettingsViewModel(session, dialogs, clipboard, toasts, launcher, hosting, ui);
        var shell = new ShellViewModel(session, live, pairing, activity, settings, navigator, dialogs, toasts);

        return new AppServices(hosting, session, shell, toasts);
    }

    public async ValueTask DisposeAsync()
    {
        _session.Dispose();
        _toasts.Dispose();
        await Hosting.Supervisor.DisposeAsync().ConfigureAwait(false);
    }
}
