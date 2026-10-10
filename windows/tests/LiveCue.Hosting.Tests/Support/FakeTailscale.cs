using System.Text;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Hosting.Tests.Support;

/// <summary>
/// A pretend Tailscale installation for the real <c>TailscaleCli</c> and <c>MappingManager</c>: a fake
/// process runner that records each argument list and plays the status documents of a scenario, and a
/// fake file system in which the executable exists. No real Tailscale command is ever run.
///
/// The serve status documents are played in order ("before", then "after"); the last one repeats.
/// </summary>
internal sealed class FakeTailscale
{
    public const string ExecutablePath = @"C:\Program Files\Tailscale\tailscale.exe";

    private readonly Lock _gate = new();
    private readonly List<IReadOnlyList<string>> _argumentLists = [];
    private readonly Queue<string> _serveDocuments = new();
    private string _lastServeDocument = ServeStatusFixtures.Empty;

    public FakeTailscale(bool installed = true)
    {
        Files.SetEnvironment("ProgramFiles", @"C:\Program Files");
        if (installed)
        {
            Files.AddFile(ExecutablePath);
        }

        Processes.Default = Play;
    }

    /// <summary>Hand this to the CLI as its process runner.</summary>
    public FakeProcessRunner Processes { get; } = new();

    /// <summary>Hand this to the CLI as its file system.</summary>
    public FakeHostFileSystem Files { get; } = new();

    /// <summary>What <c>tailscale status --json</c> prints.</summary>
    public string StatusDocument { get; set; } = ServeStatusFixtures.StatusOnline;

    /// <summary>The outcome of the two read commands: Exited, TimedOut or StartFailed.</summary>
    public ProcessOutcome ReadOutcome { get; set; } = ProcessOutcome.Exited;

    /// <summary>The exit code of the two read commands.</summary>
    public int ReadExitCode { get; set; }

    /// <summary>The outcome of a write command.</summary>
    public ProcessOutcome WriteOutcome { get; set; } = ProcessOutcome.Exited;

    /// <summary>The exit code of a write command.</summary>
    public int WriteExitCode { get; set; }

    /// <summary>Every argument list that reached the "executable", in order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ArgumentLists
    {
        get
        {
            lock (_gate)
            {
                return [.. _argumentLists];
            }
        }
    }

    /// <summary>The argument lists that are not one of the two status reads: the write commands.</summary>
    public IReadOnlyList<IReadOnlyList<string>> WriteCommands =>
        [.. ArgumentLists.Where(arguments => !IsStatusRead(arguments) && !IsServeStatusRead(arguments))];

    /// <summary>Queues the serve status documents of a scenario: "before", then "after".</summary>
    public FakeTailscale ServeStatus(params string[] documents)
    {
        lock (_gate)
        {
            foreach (var document in documents)
            {
                _serveDocuments.Enqueue(document);
            }
        }

        return this;
    }

    private static bool IsStatusRead(IReadOnlyList<string> arguments) =>
        arguments.SequenceEqual(["status", "--json"]);

    private static bool IsServeStatusRead(IReadOnlyList<string> arguments) =>
        arguments.SequenceEqual(["serve", "status", "--json"]);

    private ProcessResult Play(ProcessRequest request)
    {
        lock (_gate)
        {
            _argumentLists.Add([.. request.Arguments]);
            if (IsStatusRead(request.Arguments))
            {
                return Read(StatusDocument);
            }

            if (IsServeStatusRead(request.Arguments))
            {
                if (_serveDocuments.Count > 0)
                {
                    _lastServeDocument = _serveDocuments.Dequeue();
                }

                return Read(_lastServeDocument);
            }

            return new ProcessResult(WriteOutcome, WriteOutcome == ProcessOutcome.Exited ? WriteExitCode : -1, ReadOnlyMemory<byte>.Empty);
        }
    }

    private ProcessResult Read(string document) => ReadOutcome == ProcessOutcome.Exited
        ? new ProcessResult(ProcessOutcome.Exited, ReadExitCode, Encoding.UTF8.GetBytes(document))
        : new ProcessResult(ReadOutcome, -1, ReadOnlyMemory<byte>.Empty);
}
