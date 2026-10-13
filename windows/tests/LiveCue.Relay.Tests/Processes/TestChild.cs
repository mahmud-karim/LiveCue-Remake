using System.Diagnostics;
using System.Globalization;
using System.Text;
using LiveCue.Relay.Processes;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Processes;

/// <summary>
/// <c>LiveCue.TestChild</c> as a real child process: <c>dotnet LiveCue.TestChild.dll &lt;mode&gt; …</c>,
/// started with the dotnet host that runs the tests. No test starts anything else.
/// </summary>
internal static class TestChild
{
    /// <summary>The program, beside the test assembly.</summary>
    public static string Dll => DotnetHost.Beside("LiveCue.TestChild.dll");

    /// <summary>A request that runs the child in one of its modes. The test sets what else it needs.</summary>
    public static ProcessRequest Request(params string[] modeAndArguments) => new()
    {
        FileName = DotnetHost.Path,
        Arguments = [Dll, .. modeAndArguments],
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>True when no process with that id runs any more.</summary>
    public static bool IsGone(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>Waits, in real time, until none of the processes runs any more.</summary>
    public static async Task WaitUntilGoneAsync(params int[] processIds)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!processIds.All(IsGone))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("A child process is still running.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Waits, in real time, until the file exists and holds that many complete lines.</summary>
    public static async Task<string[]> WaitForLinesAsync(string path, int lines)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = Encoding.UTF8.GetString(ReadShared(path));
                    var found = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (found.Length >= lines && text.EndsWith('\n'))
                    {
                        return found;
                    }
                }
            }
            catch (IOException)
            {
                // The child is writing the file right now.
            }

            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The child process did not write its report in time.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    /// The bytes of a file the child may be writing right now. The file is opened so that the child can
    /// go on writing: a reader that locked it would make the child's next write fail.
    /// </summary>
    public static byte[] ReadShared(string path)
    {
        using var file = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        file.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The two ids the "spawn" mode writes: the child's own, then its grandchild's.</summary>
    public static async Task<(int Child, int Grandchild)> WaitForSpawnAsync(string path)
    {
        var lines = await WaitForLinesAsync(path, 2);
        return (int.Parse(lines[0], CultureInfo.InvariantCulture), int.Parse(lines[1], CultureInfo.InvariantCulture));
    }

    /// <summary>Ends a process a test left running on purpose. Never throws.</summary>
    public static void Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception
            is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
        }
    }
}

/// <summary>
/// The real <see cref="ProcessRunner"/> with a stand-in for Codex: a request for
/// <c>&lt;codex&gt; &lt;arguments&gt;</c> is started as
/// <c>dotnet LiveCue.TestChild.dll &lt;arguments&gt; --stub &lt;scenario&gt; --report &lt;file&gt;</c>.
/// Everything else of the request (environment policy, working folder, standard input, limits) reaches
/// the real runner unchanged, and every request is kept as it was asked for.
/// </summary>
internal sealed class StandInCodexRunner(ProcessRunner real, string scenario, string reportFile) : IProcessRunner
{
    private readonly Lock _gate = new();
    private readonly List<ProcessRequest> _requests = [];

    /// <summary>Every request as the relay made it, in order.</summary>
    public IReadOnlyList<ProcessRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct) =>
        real.RunAsync(Redirect(request), ct);

    public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct) =>
        real.StartSessionAsync(Redirect(request), ct);

    private ProcessRequest Redirect(ProcessRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }

        return request with
        {
            FileName = DotnetHost.Path,
            Arguments = [TestChild.Dll, .. request.Arguments, "--stub", scenario, "--report", reportFile],
        };
    }
}
