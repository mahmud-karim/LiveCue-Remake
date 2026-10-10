using System.IO;
using System.Text;
using LiveCue.Desktop.Interop;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// Standard output for a windowed program. LiveCue.Desktop.exe has no console of its own: when its
/// output is redirected (a pipe or a file, as in CI) writing just works, and when it is started from
/// a terminal without redirection this attaches to that terminal so the result can be read there.
/// </summary>
internal static class SelfTestConsole
{
    private static readonly Lock Gate = new();
    private static bool _prepared;

    public static void WriteLine(string line)
    {
        lock (Gate)
        {
            Prepare();
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    public static void WriteError(string line)
    {
        lock (Gate)
        {
            Prepare();
            Console.Error.WriteLine(line);
            Console.Error.Flush();
        }
    }

    private static void Prepare()
    {
        if (_prepared)
        {
            return;
        }

        _prepared = true;
        var hasOutput = NativeMethods.GetStdHandle(NativeMethods.StdOutputHandle) is not (0 or -1);
        if (hasOutput || !NativeMethods.AttachConsole(NativeMethods.AttachParentProcess))
        {
            return;
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true });
    }
}

/// <summary>The self-test's log: every line goes to standard output and into self-test.log beside the screenshots.</summary>
internal sealed class SelfTestLog(string? directory)
{
    public const string FileName = "self-test.log";

    private readonly StringBuilder _lines = new();

    public void Write(string line)
    {
        _lines.AppendLine(line);
        SelfTestConsole.WriteLine(line);
    }

    public void Save()
    {
        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, FileName), _lines.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SelfTestConsole.WriteError("Could not write the self-test log: " + exception.Message);
        }
    }
}
