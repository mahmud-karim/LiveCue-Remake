namespace LiveCue.Hosting;

/// <summary>Architecture 3.3: the advertised endpoint of a mode.</summary>
public interface IEndpointResolver
{
    /// <summary>
    /// Private: https://&lt;dns&gt;:&lt;privateHttpsPort&gt;/ . Public: https://&lt;dns&gt;:&lt;publicHttpsPort&gt;&lt;prefix&gt;/ .
    /// The port is always written. Returns the canonical form of protocol 4.3, or null when the result
    /// fails that grammar.
    /// </summary>
    string? Resolve(RelaySettings settings, string dnsName);
}
