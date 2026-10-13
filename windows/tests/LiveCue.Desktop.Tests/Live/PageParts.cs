using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Live;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Shell;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Live;

/// <summary>
/// The Live and Activity view models over one <see cref="FakeSession"/>, as the composition root wires
/// them, with a clock the test moves. Nothing here touches a window, a relay or a thread.
/// </summary>
internal sealed class PageParts
{
    public const string Endpoint = "https://pc.example.test:11443/";
    public const string RequestId = "7f3a9c12";

    public PageParts()
    {
        Live = new LiveViewModel(Session, Navigator, Dialogs, Clipboard, Toasts, Clock, Ui);
        Activity = new ActivityViewModel(Session, Clock);
    }

    public FakeSession Session { get; } = new();

    public ManualTimeProvider Clock { get; } = new();

    public ImmediateDispatcher Ui { get; } = new();

    public RecordingNavigator Navigator { get; } = new();

    public RecordingDialogHost Dialogs { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public RecordingToastHost Toasts { get; } = new();

    public LiveViewModel Live { get; }

    public ActivityViewModel Activity { get; }

    public DateTimeOffset Now => Clock.GetUtcNow();

    /// <summary>The session as it is once the relay runs: Ready, with its address.</summary>
    public void Run(ConnectionMode mode = ConnectionMode.Private)
    {
        Session.RaiseSignal(SessionSignal.RelayStarting);
        Session.AdvertisedEndpoint = Endpoint;
        Session.State = RelayUiState.Ready;
        Session.RaiseRelayEvent(new RelayStarted(Now, Endpoint, 47841, mode, 2, "1.0.0", PairingWasCreated: false));
    }

    public void Raise(RelayEvent relayEvent) => Session.RaiseRelayEvent(relayEvent);

    public RequestStarted Request(
        AnswerKind kind = AnswerKind.Assist,
        string? transcript = "If we hold the March date, what would we have to cut?",
        string? partial = "and could the analytics dashboard move to the sprint after",
        string? instruction = "answer briefly",
        string? payloadJson = null) => new(
            Now,
            RequestId,
            kind,
            "gpt-5.6-sol",
            "low",
            transcript,
            partial,
            instruction,
            payloadJson ?? """{"transcript":"If we hold the March date, what would we have to cut?","partialTranscript":"and could the analytics dashboard move to the sprint after","instruction":"answer briefly"}""");

    public RequestCompleted Reply(string payloadJson, double durationMs = 912, double codexMs = 850, AnswerKind kind = AnswerKind.Assist) =>
        new(Now, RequestId, kind, durationMs, codexMs, "gpt-5.6-sol", "low", payloadJson);

    public RequestFailed Failure(string code) =>
        new(Now, RequestId, AnswerKind.Assist, code, "The relay's own text for " + code + ".");

    /// <summary>An inspection in which the Private mapping has that state and the Public one is absent.</summary>
    public static ConnectionInspection Inspection(
        MappingState state,
        MappingRefusal blockedBy = MappingRefusal.None,
        TailscaleReadFailure failure = TailscaleReadFailure.None,
        params ExposedHandler[] exposure) => new(
            new MappingInspection(ConnectionMode.Private, state, blockedBy, InLedger: state == MappingState.Present, 11443, "/"),
            new MappingInspection(ConnectionMode.Public, MappingState.Absent, MappingRefusal.None, InLedger: false, 10000, "/livecue"),
            StaleOwned: [],
            failure,
            ManualTimeProvider.DefaultStart,
            exposure);

    /// <summary>A report in which the given checks failed and the checks that depend on them were not made.</summary>
    public static PrerequisiteReport Report(
        IReadOnlyDictionary<PrerequisiteId, PrerequisiteState> states,
        string? codexPath = @"C:\Tools\Codex\codex.exe",
        string? dnsName = "pc.example.test",
        string? codexVersion = "1.2.3",
        string? checkedVersion = "1.2.3")
    {
        var settings = RelaySettings.Default with { CodexContainmentCheckedVersion = checkedVersion };
        var results = Enum.GetValues<PrerequisiteId>()
            .Select(id =>
            {
                var state = states.GetValueOrDefault(id, PrerequisiteState.Passed);
                var detail = (id, state) switch
                {
                    (PrerequisiteId.CodexFound, PrerequisiteState.Passed) => codexPath,
                    (PrerequisiteId.TailscaleDnsName, PrerequisiteState.Passed) => dnsName,
                    _ => null,
                };
                return new PrerequisiteResult(id, state, detail);
            })
            .ToList();

        var codexFound = results[1].State == PrerequisiteState.Passed;
        return new PrerequisiteReport(
            results,
            new SettingsLoadResult(settings, SettingsFileState.Missing, []),
            codexFound ? codexPath : null,
            results[5].State == PrerequisiteState.Passed ? dnsName : null,
            codexFound ? codexVersion : null);
    }
}

/// <summary>
/// The real window on the real shell and the real page view models, over a <see cref="FakeSession"/>:
/// a test puts the session into any state and looks at what the window draws. Nothing is started,
/// bound, read or written; the window is off-screen and never takes the focus. Create and use it on
/// the application's thread (<see cref="WpfApp"/>).
/// </summary>
internal sealed class PageWindow : IDisposable
{
    private readonly ToastHost _toasts;

    public PageWindow()
    {
        var ui = new ImmediateDispatcher();
        var navigator = new Navigator();
        var dialogs = new DialogHost();
        var launcher = new FakeLauncher();
        _toasts = new ToastHost(ui, Clock);
        var hosting = HostingFactory.Create(
            new SupervisorOptions(@"C:\LiveCue-tests\data", "1.0.0"),
            new HostingOverrides { Files = new RecordingFiles(), Processes = new RecordingProcesses() });

        Live = new LiveViewModel(Session, navigator, dialogs, Clipboard, _toasts, Clock, ui);
        Activity = new ActivityViewModel(Session, Clock);
        Shell = new ShellViewModel(
            Session,
            Live,
            new PairingViewModel(Session, navigator, dialogs, Clipboard, _toasts, Clock, ui),
            Activity,
            new SettingsViewModel(Session, dialogs, Clipboard, _toasts, launcher, hosting, ui),
            navigator,
            dialogs,
            _toasts);

        Window = new MainWindow(Shell) { DrivesRelay = false, KeepActiveAppearance = true };
        WpfApp.ShowOffScreen(Window);
    }

    public FakeSession Session { get; } = new();

    public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 10, 9, 14, 2, TimeSpan.Zero));

    public FakeClipboard Clipboard { get; } = new();

    public LiveViewModel Live { get; }

    public ActivityViewModel Activity { get; }

    public ShellViewModel Shell { get; }

    public MainWindow Window { get; }

    /// <summary>The window's content: what a screenshot shows.</summary>
    public FrameworkElement Root => (FrameworkElement)Window.Content;

    public DateTimeOffset Now => Clock.GetUtcNow();

    /// <summary>The page that is shown: the content of the window's page host.</summary>
    public FrameworkElement Page =>
        Window.FindName("PageHost") as FrameworkElement ?? throw new InvalidOperationException("The window has no page host.");

    public void Show(AppPage page)
    {
        Shell.Navigate(page);
        WpfApp.Settle(TimeSpan.FromMilliseconds(260));
    }

    /// <summary>The session as it is once the relay runs and a phone was heard.</summary>
    public void Run()
    {
        Session.RaiseSignal(SessionSignal.RelayStarting);
        Session.AdvertisedEndpoint = PageParts.Endpoint;
        Session.Connection = PageParts.Inspection(MappingState.Present);
        Session.State = RelayUiState.Ready;
        Session.RaiseRelayEvent(new RelayStarted(Now, PageParts.Endpoint, 47841, ConnectionMode.Private, 2, "1.0.0", false));
        Session.LastPhoneSeen = Now;
        Session.Presence = PhonePresence.Connected;
    }

    /// <summary>Lays the window out at that size and lets it settle.</summary>
    public void Resize(double width, double height)
    {
        Window.Width = width;
        Window.Height = height;
        WpfApp.Settle(TimeSpan.FromMilliseconds(40));
        Window.UpdateLayout();
    }

    /// <summary>
    /// Draws the scene at the default and at the minimum window size. With the environment variable
    /// <c>LIVECUE_TEST_SHOTS</c> set, both are written as PNG files for a person to compare with the mockup.
    /// </summary>
    public void Capture(string scene, Action<double, double>? atEachSize = null)
    {
        foreach (var (width, height) in new[] { (1240d, 660d), (960d, 620d) })
        {
            Resize(width, height);
            Assert.Equal((width, height), (Root.ActualWidth, Root.ActualHeight));
            WpfApp.Render(Root, $"{scene}-{width}x{height}.png");
            Assert.True(VisualChecks.RequireCrispText(Root) > 0);
            VisualChecks.RequireEffectsOnlyOnCasters(Root);
            atEachSize?.Invoke(width, height);
        }

        Resize(1240, 660);
    }

    /// <summary>The element of the page with that automation id that is shown now; null when none is.</summary>
    public FrameworkElement? Find(string automationId) => WpfApp.Descendants(Page)
        .OfType<FrameworkElement>()
        .FirstOrDefault(element => element.IsVisible && AutomationProperties.GetAutomationId(element) == automationId);

    public T Shown<T>(string automationId)
        where T : FrameworkElement =>
        Find(automationId) as T
        ?? throw new InvalidOperationException($"The page shows no {typeof(T).Name} with the id {automationId}.");

    /// <summary>Every text the page shows now: text blocks and the read-only text boxes alike.</summary>
    public List<string> Texts() => WpfApp.Descendants(Page)
        .OfType<FrameworkElement>()
        .Where(element => element.IsVisible)
        .Select(element => element switch
        {
            TextBlock block => block.Text,
            TextBox box => box.Text,
            _ => null,
        })
        .Where(text => !string.IsNullOrEmpty(text))
        .Select(text => text!)
        .ToList();

    /// <summary>Where an element lies in the window, in layout units (one pixel each in this window).</summary>
    public Rect Bounds(FrameworkElement element) =>
        element.TransformToAncestor(Root).TransformBounds(new Rect(element.RenderSize));

    public void Dispose()
    {
        Window.Close();
        _toasts.Dispose();
    }
}
