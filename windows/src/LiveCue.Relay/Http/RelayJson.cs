using System.Text.Json.Serialization;

namespace LiveCue.Relay.Http;

/// <summary>Body of <c>GET /v1/health</c>.</summary>
internal sealed record HealthResponse(bool Ok, int Protocol, string RelayVersion)
{
    public static HealthResponse Current { get; } =
        new(Ok: true, RelayDefaults.ProtocolVersion, RelayDefaults.RelayVersion);
}

/// <summary>Body of every error response.</summary>
internal sealed record ErrorResponse(string Error);

/// <summary>
/// Source-generated serialisation for the relay's wire types: no reflection at run time, and the
/// property order on the wire is the declaration order above.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class RelayJsonContext : JsonSerializerContext;
