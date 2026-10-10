namespace LiveCue.Relay.Speech;

internal enum LabMessageKind
{
    /// <summary>A text message. Bytes holds it.</summary>
    Text,

    /// <summary>The lab closed the socket (a close frame, or the connection ended).</summary>
    Closed,

    /// <summary>A binary message, or a message larger than C-23.</summary>
    Invalid,
}

internal readonly record struct LabMessage(LabMessageKind Kind, ReadOnlyMemory<byte> Bytes);

/// <summary>Thrown when the lab socket cannot be opened or fails. Fixed message.</summary>
internal sealed class LabSocketException : Exception
{
    public LabSocketException()
        : base("The lab socket failed.")
    {
    }
}

/// <summary>One open socket to the lab's /relay endpoint (protocol 11.3). One sender and one receiver.</summary>
internal interface ILabSocket : IAsyncDisposable
{
    ValueTask SendAudioAsync(ReadOnlyMemory<byte> frame, CancellationToken ct);

    /// <summary>Sends exactly {"type":"endStream"} as a text message.</summary>
    ValueTask SendEndStreamAsync(CancellationToken ct);

    /// <summary>Throws <see cref="LabSocketException"/> on a transport failure.</summary>
    ValueTask<LabMessage> ReceiveAsync(CancellationToken ct);
}

/// <summary>Lab socket seam.</summary>
internal interface ILabSocketConnector
{
    /// <summary>Opens the socket within C-47. Throws <see cref="LabSocketException"/> on any failure.</summary>
    Task<ILabSocket> ConnectAsync(CancellationToken ct);
}
