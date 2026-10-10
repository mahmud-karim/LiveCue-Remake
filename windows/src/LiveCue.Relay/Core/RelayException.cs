using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Core;

/// <summary>Carries a wire error out of area code. The edge turns it into the response of that error.</summary>
internal sealed class RelayException : Exception
{
    public RelayException(RelayError error)
        : base(error.Text)
    {
        Error = error;
    }

    public RelayError Error { get; }
}
