using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Pairing;

/// <summary>
/// The Pairing view model over a <see cref="FakeSession"/>, as the composition root wires it, with a
/// clock the test moves. Nothing here touches a window, a relay or a thread.
/// </summary>
internal sealed class PairingParts
{
    public const string Endpoint = "https://pc.example.test:11443/";

    public PairingParts()
    {
        Pairing = new PairingViewModel(Session, Navigator, Dialogs, Clipboard, Toasts, Clock, Ui);
    }

    public FakeSession Session { get; } = new();

    public ManualTimeProvider Clock { get; } = new();

    public ImmediateDispatcher Ui { get; } = new();

    public RecordingNavigator Navigator { get; } = new();

    public RecordingDialogHost Dialogs { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public RecordingToastHost Toasts { get; } = new();

    public PairingViewModel Pairing { get; }

    public DateTimeOffset Now => Clock.GetUtcNow();

    /// <summary>The session as it is once the relay runs and Tailscale forwards to it.</summary>
    public void Run()
    {
        Session.AdvertisedEndpoint = Endpoint;
        Session.Connection = Inspection(MappingState.Present);
        Session.State = RelayUiState.Ready;
    }

    /// <summary>The session hands the page a matrix, as after "Show QR" or a reset.</summary>
    public bool[][] ProduceQr(int modules = QrFixture.DefaultModules)
    {
        var matrix = QrFixture.Matrix(modules);
        Session.RaisePairingQr(new PairingQr(Endpoint, matrix));
        return matrix;
    }

    public void SelfCheck(SelfCheckResult result) => Session.RaiseRelayEvent(new SelfCheckCompleted(Now, result));

    public void Stop()
    {
        Session.State = RelayUiState.Stopped;
        Session.RaiseSignal(SessionSignal.RelayEnded);
    }

    /// <summary>An inspection in which the Private mapping has that state and the Public one is absent.</summary>
    public static ConnectionInspection Inspection(MappingState state) => new(
        new MappingInspection(ConnectionMode.Private, state, MappingRefusal.None, InLedger: state == MappingState.Present, 11443, "/"),
        new MappingInspection(ConnectionMode.Public, MappingState.Absent, MappingRefusal.None, InLedger: false, 10000, "/livecue"),
        StaleOwned: [],
        TailscaleReadFailure.None,
        ManualTimeProvider.DefaultStart,
        Exposure: []);
}

/// <summary>
/// A synthetic module matrix for tests and pictures: the three finder patterns of a QR code and a
/// fixed pattern between them. It encodes nothing, so no picture of it can pair anything.
/// </summary>
internal static class QrFixture
{
    /// <summary>The side of a version 6 code, which is about what a pairing payload needs.</summary>
    public const int DefaultModules = 41;

    public static bool[][] Matrix(int modules = DefaultModules)
    {
        var matrix = new bool[modules][];
        for (var row = 0; row < modules; row++)
        {
            matrix[row] = new bool[modules];
            for (var column = 0; column < modules; column++)
            {
                // A fixed scramble of the position: looks like data, is not.
                var mixed = unchecked((uint)((row * 73856093) ^ (column * 19349663) ^ ((row + column) * 83492791)));
                matrix[row][column] = ((mixed >> 7) & 1) == 1;
            }
        }

        Finder(matrix, 0, 0);
        Finder(matrix, 0, modules - 7);
        Finder(matrix, modules - 7, 0);
        return matrix;
    }

    private static void Finder(bool[][] matrix, int top, int left)
    {
        for (var row = -1; row <= 7; row++)
        {
            for (var column = -1; column <= 7; column++)
            {
                var y = top + row;
                var x = left + column;
                if (y < 0 || x < 0 || y >= matrix.Length || x >= matrix.Length)
                {
                    continue;
                }

                var ring = Math.Max(Math.Abs(row - 3), Math.Abs(column - 3));
                matrix[y][x] = ring is 3 or <= 1;
            }
        }
    }
}
