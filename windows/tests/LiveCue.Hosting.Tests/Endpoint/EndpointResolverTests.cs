using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Endpoint;

/// <summary>Architecture 3.3: the advertised endpoint of a mode. Carried over from the spike; work package WP-W06 owns this file.</summary>
public sealed class EndpointResolverTests
{
    private readonly EndpointResolver _resolver = new();

    [Theory]
    [InlineData("pc.example.test", "https://pc.example.test:11443/")]
    [InlineData("PC.Example.Test.", "https://pc.example.test:11443/")]
    public void Private_and_public_forms(string dnsName, string expectedPrivate)
    {
        var publicSettings = new RelaySettings { ConnectionMode = ConnectionMode.Public, PublicHttpsPort = 8443, PublicPathPrefix = "/cue" };

        Assert.Equal(expectedPrivate, _resolver.Resolve(RelaySettings.Default, dnsName));
        Assert.Equal("https://pc.example.test:8443/cue/", _resolver.Resolve(publicSettings, dnsName));
        Assert.Equal("https://pc.example.test:443/", _resolver.Resolve(RelaySettings.Default with { PrivateHttpsPort = 443 }, dnsName));
    }

    [Theory]
    [InlineData("pc name")]
    [InlineData("pc_name.example.test")]
    [InlineData("-pc.example.test")]
    [InlineData("")]
    [InlineData(" ")]
    public void A_bad_name_is_null(string dnsName)
    {
        Assert.Null(_resolver.Resolve(RelaySettings.Default, dnsName));
    }
}
