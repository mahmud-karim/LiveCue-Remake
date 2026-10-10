using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Edge;

/// <summary>The ten routes, numbered as in protocol 6.1.</summary>
internal enum RouteId
{
    Health = 1,
    PairVerify = 2,
    Models = 3,
    LocalModelStatus = 4,
    LocalModelStart = 5,
    LocalModelStop = 6,
    Assist = 7,
    SessionSummary = 8,
    CancelRequest = 9,
    Speech = 10,
}

/// <param name="Target">The exact raw request target; for route 9 the prefix "/v1/requests/".</param>
internal sealed record RouteEntry(
    RouteId Id,
    string Method,
    string Target,
    bool ReadsBody,
    bool ServedWhilePaused,
    TimeSpan Deadline);

/// <summary>Step E-4 as a pure function. The foundation implements it completely.</summary>
internal static partial class RouteTable
{
    /// <summary>
    /// Ordinal, case-sensitive comparison of method and raw target with the ten rows. Route 9 matches when
    /// the rest of the target is 1 to C-03 characters of [A-Za-z0-9_-]; that rest is returned as
    /// <paramref name="requestId"/>. Returns null for anything else. Route 10 is returned for
    /// GET /v1/speech whether or not the request is an upgrade; the pipeline turns a non-upgrade into 404.
    /// </summary>
    public static RouteEntry? Match(string method, string rawTarget, out string? requestId)
    {
        requestId = null;
        if (method is null || rawTarget is null)
        {
            return null;
        }

        foreach (var entry in Rows)
        {
            if (!string.Equals(entry.Method, method, StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.Id != RouteId.CancelRequest)
            {
                if (string.Equals(entry.Target, rawTarget, StringComparison.Ordinal))
                {
                    return entry;
                }

                continue;
            }

            if (rawTarget.StartsWith(entry.Target, StringComparison.Ordinal)
                && IsRequestIdShape(rawTarget.AsSpan(entry.Target.Length)))
            {
                requestId = rawTarget[entry.Target.Length..];
                return entry;
            }
        }

        return null;
    }

    /// <summary>The ten rows of protocol 6.1, in order. Route 10 has no deadline of its own.</summary>
    public static IReadOnlyList<RouteEntry> Rows { get; } =
    [
        new(RouteId.Health, "GET", "/v1/health", ReadsBody: false, ServedWhilePaused: true, ProtocolConstants.HealthDeadline),
        new(RouteId.PairVerify, "POST", "/v1/pair/verify", ReadsBody: true, ServedWhilePaused: true, ProtocolConstants.PairVerifyDeadline),
        new(RouteId.Models, "GET", "/v1/models", ReadsBody: false, ServedWhilePaused: true, ProtocolConstants.ModelsDeadline),
        new(RouteId.LocalModelStatus, "GET", "/v1/local-model", ReadsBody: false, ServedWhilePaused: true, ProtocolConstants.LocalModelStatusDeadline),
        new(RouteId.LocalModelStart, "POST", "/v1/local-model/start", ReadsBody: true, ServedWhilePaused: false, ProtocolConstants.LocalModelStartDeadline),
        new(RouteId.LocalModelStop, "POST", "/v1/local-model/stop", ReadsBody: true, ServedWhilePaused: false, ProtocolConstants.LocalModelStopDeadline),
        new(RouteId.Assist, "POST", "/v1/assist", ReadsBody: true, ServedWhilePaused: false, ProtocolConstants.AssistDeadline),
        new(RouteId.SessionSummary, "POST", "/v1/session-summary", ReadsBody: true, ServedWhilePaused: false, ProtocolConstants.SessionSummaryDeadline),
        new(RouteId.CancelRequest, "DELETE", "/v1/requests/", ReadsBody: false, ServedWhilePaused: true, ProtocolConstants.CancelDeadline),
        new(RouteId.Speech, "GET", "/v1/speech", ReadsBody: false, ServedWhilePaused: false, Timeout.InfiniteTimeSpan),
    ];

    /// <summary>The row of <paramref name="id"/>.</summary>
    public static RouteEntry Row(RouteId id) => Rows[(int)id - 1];

    /// <summary>C-03, checked by hand: 1 to RequestIdMaxLength characters of [A-Za-z0-9_-].</summary>
    public static bool IsRequestIdShape(ReadOnlySpan<char> text)
    {
        if (text.Length is 0 or > ProtocolConstants.RequestIdMaxLength)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
