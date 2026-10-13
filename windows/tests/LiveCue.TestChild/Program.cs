using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

// LiveCue.TestChild: a child process for the process-runner tests. One mode per run:
//
//   echo-args <a> <b> ...        one line per argument, exactly as received
//   env-names                    the name of every environment variable, one per line, sorted
//   stdin                        copies standard input to standard output, then ends
//   write <n>                    writes n bytes ('x') to standard output
//   sleep <milliseconds>         sleeps, then ends
//   exit <code>                  ends with that exit code
//   spawn <file> <milliseconds>  starts a grandchild that sleeps, writes both process ids to the file
//                                (its own, then the grandchild's), then sleeps itself
//   app-server ...               a stand-in for `codex app-server --listen stdio://` (protocol 12.1)
//   exec ...                     a stand-in for `codex exec` (protocol 12.2)
//
// The two stand-ins take what a test appends after the real arguments:
//
//   --stub <scenario>            what to play (see StubAppServer and StubExec)
//   --report <file>              where to write what the test wants to see: the process ids, the lines
//                                or the prompt received, the environment names
//
// Nothing here calls a model, and nothing is read or written outside the paths a test passes.

if (args.Length == 0)
{
    return Usage();
}

using var output = Console.OpenStandardOutput();
switch (args[0])
{
    case "echo-args":
        foreach (var argument in args.AsSpan(1))
        {
            PrintLine(output, argument);
        }

        return 0;

    case "env-names":
        foreach (var name in EnvironmentNames())
        {
            PrintLine(output, name);
        }

        return 0;

    case "stdin":
        using (var input = Console.OpenStandardInput())
        {
            input.CopyTo(output);
        }

        return 0;

    case "write" when args.Length == 2 && TryNumber(args[1], out var count):
        var block = new byte[8192];
        Array.Fill(block, (byte)'x');
        for (var left = count; left > 0; left -= block.Length)
        {
            output.Write(block, 0, Math.Min(left, block.Length));
        }

        output.Flush();
        return 0;

    case "sleep" when args.Length == 2 && TryNumber(args[1], out var milliseconds):
        Thread.Sleep(milliseconds);
        return 0;

    case "exit" when args.Length == 2 && TryNumber(args[1], out var code):
        return code;

    case "spawn" when args.Length == 3 && TryNumber(args[2], out var lifetime):
        File.WriteAllLines(args[1], [Id(Environment.ProcessId), Id(SpawnSleeper(lifetime))]);
        Thread.Sleep(lifetime);
        return 0;

    case "app-server":
        return StubAppServer(args, output);

    case "exec":
        return StubExec(args);

    default:
        return Usage();
}

static bool TryNumber(string text, out int value) =>
    int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

static string Id(int processId) => processId.ToString(CultureInfo.InvariantCulture);

/// One line as UTF-8 with a line feed, whatever the console's own encoding is.
static void PrintLine(Stream output, string line)
{
    output.Write(Encoding.UTF8.GetBytes(line + "\n"));
    output.Flush();
}

static IEnumerable<string> EnvironmentNames() =>
    Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.OrdinalIgnoreCase);

/// Adds one line to a report file. A test reads the file while it grows, so a moment in which the file
/// cannot be opened is waited out instead of ending this program.
static void AppendLine(string path, string line)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            file.Write(Encoding.UTF8.GetBytes(line + "\n"));
            return;
        }
        catch (IOException) when (attempt < 200)
        {
            Thread.Sleep(10);
        }
    }
}

/// The value that follows a named option, or null.
static string? Option(string[] arguments, string name)
{
    var index = Array.LastIndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

/// Starts this program again in "sleep" mode and returns the new process's id. The grandchild inherits
/// this process's standard handles, as a helper of a real tool would.
static int SpawnSleeper(int milliseconds)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("sleep");
    start.ArgumentList.Add(milliseconds.ToString(CultureInfo.InvariantCulture));
    using var grandchild = Process.Start(start)!;
    return grandchild.Id;
}

/// A stand-in for the Codex app-server. Scenarios:
///
///   ok (default)   answers `initialize` and `model/list` between lines a reader must skip, then waits
///   early-exit     answers `initialize`, then ends without answering `model/list`
///   silent         answers nothing and waits
///   flood          answers `initialize`, then writes more than 1 MiB and waits
///   error          answers `model/list` with an error
///
/// With --report, the file gets this process's id on its first line and then every line received.
static int StubAppServer(string[] arguments, Stream output)
{
    var scenario = Option(arguments, "--stub") ?? "ok";
    var report = Option(arguments, "--report");
    if (report is not null)
    {
        AppendLine(report, Id(Environment.ProcessId));
    }

    void Print(string line) => PrintLine(output, line);

    using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
    while (input.ReadLine() is { } line)
    {
        if (report is not null)
        {
            AppendLine(report, line);
        }

        if (scenario == "silent")
        {
            continue;
        }

        string? method = null;
        try
        {
            using var message = JsonDocument.Parse(line);
            method = message.RootElement.TryGetProperty("method", out var name) ? name.GetString() : null;
        }
        catch (JsonException)
        {
        }

        switch (method)
        {
            case "initialize":
                Print("codex stand-in: starting");
                Print("""{"method":"notice","params":{"id":1}}""");
                Print("""{"id":1,"result":{"userAgent":"stand-in"}}""");
                if (scenario == "flood")
                {
                    var junk = new string('j', 64 * 1024);
                    for (var written = 0; written <= 1_048_576; written += junk.Length)
                    {
                        Print(junk);
                    }
                }

                break;

            case "model/list" when scenario == "early-exit":
                return 0;

            case "model/list" when scenario == "error":
                Print("""{"id":2,"error":{"code":-32000,"message":"stand-in failure"}}""");
                break;

            case "model/list":
                Print("""{"id":7,"result":{"data":[{"model":"gpt-wrong-reply"}]}}""");
                Print(
                    """{"id":2,"result":{"data":["""
                    + """{"model":"gpt-stand-in","displayName":"Stand-in","hidden":false,"supportedReasoningEfforts":[{"reasoningEffort":"low","description":"x"},{"reasoningEffort":"high"}]},"""
                    + """{"displayName":"No model"},"""
                    + """{"model":"gpt-hidden","hidden":true,"supportedReasoningEfforts":[{"reasoningEffort":"low"}]}"""
                    + """],"nextCursor":"more"}}""");
                break;

            default:
                break;
        }
    }

    // Standard input was closed. A real app-server would end; this one waits, so that only a kill ends it.
    Thread.Sleep(Timeout.Infinite);
    return 0;
}

/// A stand-in for `codex exec`. It reads the prompt from standard input and finds `-o <file>` among its
/// arguments. Scenarios:
///
///   ok (default)   writes a valid output file (a summary when the schema path names one) and ends with 0
///   fail           writes the output file and ends with 3
///   no-output      ends with 0 without writing the file
///   big            writes an output file of more than 1 MiB and ends with 0
///   hang           writes a file into its folder, starts a grandchild and waits
///
/// With --report, the file gets one JSON object: the process ids, the working folder, the arguments,
/// the prompt and the environment names.
static int StubExec(string[] arguments)
{
    var scenario = Option(arguments, "--stub") ?? "ok";
    var report = Option(arguments, "--report");
    var outputFile = Option(arguments, "-o");
    var schema = Option(arguments, "--output-schema") ?? string.Empty;

    string prompt;
    using (var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
    {
        prompt = input.ReadToEnd();
    }

    var grandchild = 0;
    if (scenario == "hang")
    {
        File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "scratch.txt"), prompt);
        grandchild = SpawnSleeper(600_000);
    }

    if (report is not null)
    {
        using var file = File.Create(report);
        using var writer = new Utf8JsonWriter(file);
        writer.WriteStartObject();
        writer.WriteNumber("processId", Environment.ProcessId);
        writer.WriteNumber("grandchildId", grandchild);
        writer.WriteString("workingFolder", Environment.CurrentDirectory);
        writer.WriteString("prompt", prompt);
        writer.WriteStartArray("arguments");
        foreach (var argument in arguments)
        {
            writer.WriteStringValue(argument);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("environment");
        foreach (var name in EnvironmentNames())
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    const string Assist =
        """{"detectedQuestion":"Stand-in question?","answer":"Stand-in answer.","details":"","memory":{"summary":"Stand-in memory.","facts":[],"openQuestions":[],"throughSegmentId":"11111111-2222-3333-4444-555555555555"},"extra":"dropped"}""";
    const string Summary =
        """{"title":"Stand-in title","summary":"Stand-in summary.","keyPoints":["Point."],"actionItems":[]}""";

    switch (scenario)
    {
        case "hang":
            Thread.Sleep(Timeout.Infinite);
            return 0;

        case "no-output":
            return 0;

        case "big" when outputFile is not null:
            File.WriteAllText(outputFile, "{\"pad\":\"" + new string('p', 1_048_576) + "\"}");
            return 0;

        default:
            if (outputFile is not null)
            {
                File.WriteAllText(outputFile, schema.Contains("summary", StringComparison.Ordinal) ? Summary : Assist);
            }

            return scenario == "fail" ? 3 : 0;
    }
}

static int Usage()
{
    Console.Error.WriteLine(
        "Usage: LiveCue.TestChild echo-args|env-names|stdin|write <n>|sleep <ms>|exit <code>|spawn <file> <ms>|app-server|exec");
    return 64;
}
