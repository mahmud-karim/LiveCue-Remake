using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// The byte allowance of C-72 (protocol 10.5): the audio bytes a phone has sent since <c>ready</c> may
/// never be more than <c>160000 + 35 × (milliseconds since ready)</c>. That is ten seconds of audio at
/// once, and then a little more than real time (32 bytes per millisecond) for ever after.
///
/// Time comes from the relay's clock. One stream owns one limiter and calls it from its loop only.
/// </summary>
internal sealed class AudioLimiter
{
    private readonly TimeProvider _clock;
    private long _readyTimestamp;
    private long _acceptedBytes;

    public AudioLimiter(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _readyTimestamp = clock.GetTimestamp();
    }

    /// <summary>The bytes accepted since <see cref="Start"/>.</summary>
    public long AcceptedBytes => _acceptedBytes;

    /// <summary>
    /// The allowance of C-72 at a moment: the base, plus the rate for every whole millisecond since
    /// <c>ready</c>. A part of a millisecond earns nothing yet.
    /// </summary>
    public static long AllowedBytes(TimeSpan sinceReady)
    {
        var milliseconds = sinceReady <= TimeSpan.Zero ? 0 : (long)sinceReady.TotalMilliseconds;
        return ProtocolConstants.AllowanceBaseBytes + (ProtocolConstants.AllowanceBytesPerMs * milliseconds);
    }

    /// <summary>The <c>ready</c> frame is being sent: the count starts at zero, now.</summary>
    public void Start()
    {
        _readyTimestamp = _clock.GetTimestamp();
        _acceptedBytes = 0;
    }

    /// <summary>
    /// Counts one audio frame. False, and nothing counted, when the total would pass the allowance of
    /// this moment.
    /// </summary>
    public bool TryAccept(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        var total = _acceptedBytes + bytes;
        if (total > AllowedBytes(_clock.GetElapsedTime(_readyTimestamp)))
        {
            return false;
        }

        _acceptedBytes = total;
        return true;
    }
}
