using System.Text;
using LiveCue.Relay.Host;

// LiveCueRelayHost.exe: the relay supervisor without a window (architecture 3.14). It is for headless
// use and for the manual GPU check, not the everyday entry point. Everything is in HostProgram, which
// the tests call directly.

using var stopRequested = new CancellationTokenSource();
Console.CancelKeyPress += (_, cancel) =>
{
    // Ctrl+C is a clean stop, not a kill.
    cancel.Cancel = true;
    stopRequested.Cancel();
};

// Read once, here: --show-pairing prints the token of the real data folder to an interactive console only.
var redirected = Console.IsOutputRedirected;

// The terminal QR is drawn with block characters, which a console shows reliably only as UTF-8. The
// console's own encoding is put back afterwards. A redirected output is left as it is.
Encoding? consoleEncoding = null;
if (!redirected && args.Contains("--show-pairing", StringComparer.Ordinal))
{
    try
    {
        consoleEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        consoleEncoding = null;
    }
}

try
{
    return await HostProgram.RunAsync(
        args, HostProgram.CreateServices, new HostOutput(Console.Out, redirected), stopRequested.Token);
}
finally
{
    if (consoleEncoding is not null)
    {
        try
        {
            Console.OutputEncoding = consoleEncoding;
        }
        catch (IOException)
        {
            // The console is gone; there is nothing to put back.
        }
    }
}
