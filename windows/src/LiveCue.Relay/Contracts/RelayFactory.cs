namespace LiveCue.Relay;

/// <summary>The only way to obtain a relay.</summary>
public static class RelayFactory
{
    /// <summary>
    /// Creates an instance in state Stopped. Does no I/O and never throws for bad option values;
    /// <see cref="IRelay.StartAsync"/> reports them. Throws only <see cref="ArgumentNullException"/>.
    /// </summary>
    public static IRelay Create(RelayOptions options, RelayDependencies? dependencies = null)
        => Core.RelayHost.Create(options, dependencies ?? new RelayDependencies());
}

/// <summary>Pairing work that needs no running relay: the recovery from an unreadable pairing file.</summary>
public static class PairingMaintenance
{
    /// <summary>
    /// Replaces <c>pairing.json</c> in <paramref name="dataDirectory"/> with a new pairing, atomically,
    /// whatever the old file contained. Must not be called while a relay runs on that folder (the supervisor
    /// guarantees it). Throws <see cref="IOException"/> with a fixed message when the file cannot be written.
    /// </summary>
    public static Task ResetStoredPairingAsync(string dataDirectory, CancellationToken ct)
        => Pairing.StoredPairingReset.RunAsync(dataDirectory, protector: null, files: null, ct);
}

/// <summary>
/// The pairing payload of the stored pairing, read without a relay: what the console host's
/// <c>--show-pairing</c> prints. It binds no port and only reads, so it may be called while a relay (the
/// desktop app) runs on the same folder. <c>LiveCue.Desktop</c> never references this type; a test proves it.
/// </summary>
public static class StoredPairingExport
{
    /// <summary>
    /// Reads <c>pairing.json</c> in <paramref name="dataDirectory"/>, decrypts the token and returns the
    /// payload text of protocol 4.2 for <paramref name="advertisedEndpoint"/> in canonical form. Returns
    /// null when no pairing file exists. The file is never modified. Throws <see cref="ArgumentException"/>
    /// for an endpoint that fails the grammar of protocol 4.3, and <see cref="IOException"/> with a fixed
    /// message when the file exists and cannot be read.
    /// </summary>
    public static Task<string?> ReadPayloadAsync(string dataDirectory, string advertisedEndpoint, CancellationToken ct)
        => Pairing.StoredPairingPayload.ReadAsync(dataDirectory, advertisedEndpoint, protector: null, files: null, ct);
}
