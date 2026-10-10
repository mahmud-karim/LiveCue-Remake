namespace LiveCue.Relay.LocalModel;

/// <summary>The four members of the lab's health reply that the relay uses (protocol 11.2).</summary>
internal sealed record LabHealth(bool Ready, string? Model, bool Busy, int RelayProtocol);

/// <summary>Lab seam: probe, start, stop (protocol 9.2). Never throws except for cancellation.</summary>
internal interface ILabRuntime
{
    /// <summary>Null means "no lab": any failure, time-out or unreadable reply.</summary>
    Task<LabHealth?> ProbeAsync(CancellationToken ct);

    /// <summary>Runs the start script for one of C-04. True for exit code 0.</summary>
    Task<bool> StartAsync(string model, CancellationToken ct);

    /// <summary>Runs the stop script. True for exit code 0.</summary>
    Task<bool> StopAsync(CancellationToken ct);
}
