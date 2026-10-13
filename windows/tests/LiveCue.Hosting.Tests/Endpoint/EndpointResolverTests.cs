using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Endpoint;

/// <summary>Architecture 3.3: the advertised endpoint of a mode (W-design 4.16, test plan 2.3).</summary>
public sealed class EndpointResolverTests
{
    private readonly EndpointResolver _resolver = new();

    [Theory]
    [InlineData("pc.example.test")]
    [InlineData("PC.Example.Test")]
    [InlineData("pc.example.test.")]
    public void Private_and_public_forms(string dnsName)
    {
        var publicSettings = new RelaySettings { ConnectionMode = ConnectionMode.Public, PublicHttpsPort = 8443, PublicPathPrefix = "/cue" };

        // Private: https://<dns>:<privateHttpsPort>/ . Public: https://<dns>:<publicHttpsPort><prefix>/ .
        Assert.Equal("https://pc.example.test:11443/", _resolver.Resolve(RelaySettings.Default, dnsName));
        Assert.Equal("https://pc.example.test:8443/cue/", _resolver.Resolve(publicSettings, dnsName));
        Assert.Equal(
            "https://pc.example.test:10000/livecue/",
            _resolver.Resolve(RelaySettings.Default with { ConnectionMode = ConnectionMode.Public }, dnsName));

        // The port is always written, the default port of HTTPS included.
        Assert.Equal(
            "https://pc.example.test:443/livecue/",
            _resolver.Resolve(RelaySettings.Default with { ConnectionMode = ConnectionMode.Public, PublicHttpsPort = 443 }, dnsName));
        Assert.Equal("https://pc.example.test:443/", _resolver.Resolve(RelaySettings.Default with { PrivateHttpsPort = 443 }, dnsName));

        // Only the selected mode's port and prefix are used.
        Assert.Equal("https://pc.example.test:11443/", _resolver.Resolve(publicSettings with { ConnectionMode = ConnectionMode.Private }, dnsName));
    }

    [Theory]
    [InlineData("pc name")]
    [InlineData("pc_name.example.test")]
    [InlineData("-pc.example.test")]
    [InlineData("pc..example.test")]
    [InlineData("pc.example.test..")]
    [InlineData("pc.example.test:8443")]
    [InlineData("pc.example.test/path")]
    [InlineData("pc.example.test\n")]
    [InlineData(" pc.example.test")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    public void A_bad_name_is_null(string dnsName)
    {
        Assert.Null(_resolver.Resolve(RelaySettings.Default, dnsName));
        Assert.Null(_resolver.Resolve(RelaySettings.Default with { ConnectionMode = ConnectionMode.Public }, dnsName));
    }

    [Fact]
    public void Settings_that_make_no_valid_endpoint_give_null()
    {
        var name = "pc.example.test";

        Assert.Null(_resolver.Resolve(RelaySettings.Default with { PrivateHttpsPort = 0 }, name));
        Assert.Null(_resolver.Resolve(RelaySettings.Default with { PrivateHttpsPort = 65536 }, name));
        Assert.Null(_resolver.Resolve(
            RelaySettings.Default with { ConnectionMode = ConnectionMode.Public, PublicPathPrefix = "livecue" }, name));
        Assert.Null(_resolver.Resolve(
            RelaySettings.Default with { ConnectionMode = ConnectionMode.Public, PublicPathPrefix = "/live cue" }, name));
        Assert.Null(_resolver.Resolve(RelaySettings.Default, null!));
    }
}
