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

return await HostProgram.RunAsync(args, HostProgram.CreateServices, Console.Out, stopRequested.Token);
