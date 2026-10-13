using System.Text.Json;

namespace LiveCue.Relay.Answers;

/// <summary>
/// The four timings of the <c>execution</c> member (protocol 8.3), in milliseconds. Each measured value
/// is rounded to one decimal, and the overhead is computed from the rounded values, so that the
/// identities of the protocol hold in what is written: <c>relayTotalMs</c> is never below
/// <c>codexMs</c>, and <c>relayOverheadMs</c> is their difference.
/// </summary>
/// <param name="CodexMs">Spent in the answer run.</param>
/// <param name="RelayTotalMs">From the arrival of the request headers to the return of the run.</param>
/// <param name="RequestReadMs">From the arrival of the request headers to the parsed body.</param>
/// <param name="RelayOverheadMs"><c>max(0, RelayTotalMs - CodexMs)</c>.</param>
internal readonly record struct ExecutionTimings(
    double CodexMs,
    double RelayTotalMs,
    double RequestReadMs,
    double RelayOverheadMs)
{
    public static ExecutionTimings From(double codexMs, double relayTotalMs, double requestReadMs)
    {
        var codex = Round(codexMs);
        var total = Math.Max(Round(relayTotalMs), codex);
        return new ExecutionTimings(codex, total, Round(requestReadMs), Math.Max(0, Round(total - codex)));
    }

    /// <summary>Writes the <c>execution</c> object: the selection that was used, then the timings.</summary>
    public void Write(Utf8JsonWriter writer, string model, string reasoningEffort)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject("execution");
        writer.WriteString("model", model);
        writer.WriteString("reasoningEffort", reasoningEffort);
        writer.WriteNumber("codexMs", CodexMs);
        writer.WriteNumber("relayTotalMs", RelayTotalMs);
        writer.WriteNumber("requestReadMs", RequestReadMs);
        writer.WriteNumber("relayOverheadMs", RelayOverheadMs);
        writer.WriteEndObject();
    }

    /// <summary>One decimal; a value that is not a non-negative number counts as zero.</summary>
    private static double Round(double milliseconds) =>
        double.IsFinite(milliseconds) && milliseconds > 0
            ? Math.Round(milliseconds, 1, MidpointRounding.AwayFromZero)
            : 0;
}
