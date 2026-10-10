using System.Globalization;
using LiveCue.Relay.Core;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// Admission steps S-1 to S-6 of protocol 10.2, in that order; the first failure is the answer. The
/// socket was already accepted when these run, so a refusal reaches the phone as a typed <c>error</c>
/// frame and a close with 1008.
///
/// Nothing is started for a refused socket: no prepare, no lab connection. Step S-2 compares the header
/// with the two names of C-04 and hands on the constant, never the text the phone sent.
/// </summary>
internal static class SpeechAdmission
{
    private static readonly string ExpectedProtocol =
        ProtocolConstants.ProtocolVersion.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Null when the stream is admitted: then <paramref name="model"/> is the member of C-04 that was
    /// asked for and <paramref name="lease"/> is the speech lease, taken. Otherwise the error of the
    /// first step that failed; nothing was taken.
    /// </summary>
    public static RelayError? Admit(
        SpeechConnection connection,
        IAccessState access,
        ILocalModelController localModel,
        SpeechLease speechLease,
        out string model,
        out IDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(localModel);
        ArgumentNullException.ThrowIfNull(speechLease);
        model = string.Empty;
        lease = null;

        // S-1.
        if (!string.Equals(connection.ProtocolHeader, ExpectedProtocol, StringComparison.Ordinal))
        {
            return RelayErrors.ProtocolMismatch;
        }

        // S-2.
        if (KnownModel(connection.ModelHeader) is not { } known)
        {
            return RelayErrors.InvalidSpeechModel;
        }

        // S-3.
        if (!access.Accepting)
        {
            return RelayErrors.RelayPaused;
        }

        // S-4.
        if (!localModel.IsConfigured)
        {
            return RelayErrors.LabNotConfigured;
        }

        // S-5.
        if (localModel.OperationRunning)
        {
            return RelayErrors.ModelOperationInProgress;
        }

        // S-6. Passing this step takes the lease.
        if (speechLease.TryAcquire() is not { } taken)
        {
            return RelayErrors.SpeechBusy;
        }

        model = known;
        lease = taken;
        return null;
    }

    /// <summary>The member of C-04 that the header equals exactly, or null.</summary>
    private static string? KnownModel(string? header)
    {
        if (header is null)
        {
            return null;
        }

        foreach (var known in ProtocolConstants.SpeechModels)
        {
            if (string.Equals(header, known, StringComparison.Ordinal))
            {
                return known;
            }
        }

        return null;
    }
}
