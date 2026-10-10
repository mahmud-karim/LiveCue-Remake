using System.Globalization;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Hosting;

/// <summary>
/// Builds the advertised endpoint (architecture 3.3) from the connection mode, the ports, the prefix
/// and this PC's Tailscale DNS name, in the canonical form of protocol 4.3:
///
/// <list type="table">
/// <item><term>Private</term><description><c>https://&lt;dns name&gt;:&lt;privateHttpsPort&gt;/</c></description></item>
/// <item><term>Public</term><description><c>https://&lt;dns name&gt;:&lt;publicHttpsPort&gt;&lt;publicPathPrefix&gt;/</c></description></item>
/// </list>
/// </summary>
public static class EndpointResolver
{
    /// <summary>
    /// Stands in for this PC's Tailscale DNS name until the Tailscale status lookup is built. It is a
    /// reserved test name, so an address made from it can never reach a real machine.
    /// </summary>
    public const string PlaceholderDnsName = "pc.example.test";

    /// <summary>The canonical endpoint for <paramref name="settings"/> on the machine named <paramref name="dnsName"/>.</summary>
    /// <param name="settings">Valid settings.</param>
    /// <param name="dnsName">As Tailscale reports it; a trailing dot and upper case are tolerated.</param>
    /// <exception cref="ArgumentException">The name does not make a valid endpoint.</exception>
    public static string Resolve(HostSettings settings, string dnsName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(dnsName);

        var host = dnsName.Trim().TrimEnd('.').ToLowerInvariant();
        var candidate = settings.ConnectionMode == ConnectionMode.Public
            ? string.Create(CultureInfo.InvariantCulture, $"https://{host}:{settings.PublicHttpsPort}{settings.PublicPathPrefix}/")
            : string.Create(CultureInfo.InvariantCulture, $"https://{host}:{settings.PrivateHttpsPort}/");

        return EndpointGrammar.TryCanonicalize(candidate, out var canonical)
            ? canonical
            : throw new ArgumentException("The PC name does not make a valid endpoint.", nameof(dnsName));
    }
}
