using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.ViewModels;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop;

/// <summary>
/// The composition root: builds the supervisor and the view models for one run of the app. The normal
/// start-up and the self-test both go through here, so the self-test exercises the real view models
/// and the real supervisor, with a scripted relay and an in-memory settings store in place of the
/// real ones.
/// </summary>
internal sealed class AppServices : IAsyncDisposable
{
    private AppServices(RelaySupervisor supervisor, ShellViewModel shell, ActivityLog activity)
    {
        Supervisor = supervisor;
        Shell = shell;
        Activity = activity;
    }

    public RelaySupervisor Supervisor { get; }

    public ShellViewModel Shell { get; }

    public ActivityLog Activity { get; }

    /// <summary>The real application: settings.json in the data folder and the real relay.</summary>
    public static AppServices CreateForUser(IUiDispatcher ui)
    {
        var dataDirectory = DataFolder.Resolve();
        return Create(ui, new FileSettingsStore(dataDirectory), dataDirectory, createRelay: null, TimeProvider.System);
    }

    /// <summary>Wires the view models to a supervisor built from the given parts.</summary>
    /// <param name="ui">Where view-model state changes run.</param>
    /// <param name="settings">Where the settings come from.</param>
    /// <param name="dataDirectory">The data folder handed to the relay.</param>
    /// <param name="createRelay">Makes the relay for one run; null means the real one.</param>
    /// <param name="clock">The clock behind activity times and the phone's idle timer.</param>
    /// <param name="overrides">Per-run overrides for the supervisor; tests use it to ask for a free port.</param>
    public static AppServices Create(
        IUiDispatcher ui,
        ISettingsStore settings,
        string dataDirectory,
        Func<RelayOptions, IRelay>? createRelay,
        TimeProvider clock,
        SupervisorOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        var supervisor = new RelaySupervisor(settings, dataDirectory, createRelay, overrides, clock);
        var activity = new ActivityLog(clock);
        var relay = new RelayViewModel(supervisor, ui, activity, clock);
        var shell = new ShellViewModel(relay, activity, new SettingsPageViewModel(settings.Load()));
        return new AppServices(supervisor, shell, activity);
    }

    public async ValueTask DisposeAsync()
    {
        Shell.Relay.Dispose();
        await Supervisor.DisposeAsync().ConfigureAwait(false);
    }
}
