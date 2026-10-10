using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Protocol;

/// <summary>Body of <c>GET /v1/health</c> (protocol 6.2). All three members are always present.</summary>
internal sealed record HealthResponse(bool Ok, int Protocol, string RelayVersion)
{
    public static HealthResponse Current { get; } =
        new(Ok: true, ProtocolConstants.ProtocolVersion, ProtocolConstants.RelayVersion);
}

/// <summary>Body of every response of status 400 or above (protocol 5.3): a fixed sentence and a stable code.</summary>
internal sealed record ErrorResponse(string Error, string Code);

/// <summary>
/// A wire error of protocol 14.1: status, code and the relay's fixed English text. The text never
/// carries internal detail. Only the errors the relay can produce so far are listed.
/// </summary>
internal sealed record WireError(int Status, string Code, string Text)
{
    public static readonly WireError Unauthorized =
        new(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Invalid pairing token.");

    public static readonly WireError NotFound =
        new(StatusCodes.Status404NotFound, "NOT_FOUND", "Endpoint not found.");

    public static readonly WireError ProtocolMismatch =
        new(StatusCodes.Status400BadRequest, "PROTOCOL_MISMATCH", "Protocol version mismatch.");

    public static readonly WireError InvalidRequest =
        new(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request is not valid for this endpoint.");

    public ErrorResponse Body { get; } = new(Text, Code);
}

/// <summary>
/// Source-generated serialisation for the wire records: no reflection at run time, and members are
/// written in declaration order, which the protocol fixes.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class WireJsonContext : JsonSerializerContext;
