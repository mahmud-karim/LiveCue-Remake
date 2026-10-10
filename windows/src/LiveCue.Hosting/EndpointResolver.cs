using System.Globalization;
using LiveCue.Relay;
using LiveCue.Relay.Pairing;

namespace LiveCue.Hosting;

/// <summary>
/// Builds the advertised endpoint (architecture 3.3) from the mode, the ports, the prefix and this PC's
/// Tailscale DNS name, in the canonical form of protocol 4.3. The port is always written.
/// </summary>
internal sealed class EndpointResolver : IEndpointResolver
{
    public string? Resolve(RelaySettings settings, string dnsName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(dnsName))
        {
            return null;
        }

        var host = dnsName.Trim().TrimEnd('.');
        var candidate = settings.ConnectionMode == ConnectionMode.Public
            ? string.Create(CultureInfo.InvariantCulture, $"https://{host}:{settings.PublicHttpsPort}{settings.PublicPathPrefix}/")
            : string.Create(CultureInfo.InvariantCulture, $"https://{host}:{settings.PrivateHttpsPort}/");

        return AdvertisedEndpoint.TryCanonicalize(candidate, out var canonical) ? canonical : null;
    }
}
