using System.Globalization;
using LiveCue.Relay;
using LiveCue.Relay.Pairing;

namespace LiveCue.Hosting;

/// <summary>
/// Builds the advertised endpoint (architecture 3.3) from the mode, the ports, the prefix and this PC's
/// Tailscale DNS name:
///
/// <code>
/// Private   https://&lt;dns name&gt;:&lt;privateHttpsPort&gt;/
/// Public    https://&lt;dns name&gt;:&lt;publicHttpsPort&gt;&lt;publicPathPrefix&gt;/
/// </code>
///
/// The port is always written. The result is the canonical form of protocol 4.3, or null when what was
/// built fails that grammar. The name is used as it is given, apart from one trailing dot (the form in
/// which Tailscale reports it): a name with a space or a line break in it gives null, not a cleaned name.
/// </summary>
internal sealed class EndpointResolver : IEndpointResolver
{
    public string? Resolve(RelaySettings settings, string dnsName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrEmpty(dnsName))
        {
            return null;
        }

        var host = dnsName.EndsWith('.') ? dnsName[..^1] : dnsName;
        var candidate = settings.ConnectionMode == ConnectionMode.Public
            ? string.Create(CultureInfo.InvariantCulture, $"https://{host}:{settings.PublicHttpsPort}{settings.PublicPathPrefix}/")
            : string.Create(CultureInfo.InvariantCulture, $"https://{host}:{settings.PrivateHttpsPort}/");

        return AdvertisedEndpoint.TryCanonicalize(candidate, out var canonical) ? canonical : null;
    }
}
