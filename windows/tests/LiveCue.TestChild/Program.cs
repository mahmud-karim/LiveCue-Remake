using System.Globalization;

// LiveCue.TestChild: a child process for the process-runner tests. One mode per run:
//
//   echo-args <a> <b> ...   one line per argument, exactly as received
//   env-names               the name of every environment variable, one per line, sorted
//   stdin                   copies standard input to standard output, then ends
//   write <n>               writes n bytes ('x') to standard output
//   sleep <milliseconds>    sleeps, then ends
//   exit <code>             ends with that exit code
//
// First version (foundation). Work package WP-W03 owns this file and adds what its tests need
// (spawning a grandchild, playing a stub Codex app-server).

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
            Console.Out.WriteLine(argument);
        }

        return 0;

    case "env-names":
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.OrdinalIgnoreCase))
        {
            Console.Out.WriteLine(name);
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

    default:
        return Usage();
}

static bool TryNumber(string text, out int value) =>
    int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

static int Usage()
{
    Console.Error.WriteLine("Usage: LiveCue.TestChild echo-args|env-names|stdin|write <n>|sleep <ms>|exit <code>");
    return 64;
}
