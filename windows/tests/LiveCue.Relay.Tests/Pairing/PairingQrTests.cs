using System.Reflection;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// Protocol P2-144, last sentence: the object that carries the matrix to the desktop has no token
/// member. The desktop may hold the advertised endpoint and the modules, and nothing else of a pairing.
/// </summary>
public sealed class PairingQrTests
{
    [Fact]
    public async Task The_record_has_no_token_member()
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string[] forbidden = ["token", "payload", "secret", "authorization", "nonce"];

        // The shape: two properties, and no field that is not the storage of one of them.
        var properties = typeof(PairingQr).GetProperties(Everything)
            .Where(property => property.Name != "EqualityContract")
            .ToList();
        Assert.Equal(["Endpoint", "Modules"], properties.Select(property => property.Name).Order());
        Assert.Equal(typeof(string), typeof(PairingQr).GetProperty("Endpoint")!.PropertyType);
        Assert.Equal(typeof(bool[][]), typeof(PairingQr).GetProperty("Modules")!.PropertyType);
        Assert.Equal(2, typeof(PairingQr).GetFields(Everything).Length);
        Assert.All(
            typeof(PairingQr).GetMembers(Everything),
            member => Assert.DoesNotContain(
                forbidden,
                word => member.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));

        // The instance a host receives: the canonical endpoint, a matrix, and no trace of the token.
        await using var host = await RelayTestHost.StartAsync();
        var oldToken = host.Token;
        var shown = await host.Relay.ShowPairingAsync(CancellationToken.None);
        var reset = await host.Relay.ResetPairingAsync(CancellationToken.None);
        var newToken = await host.ReadTokenAsync();

        Assert.Equal(RelayTestHost.Endpoint, shown.Endpoint);
        Assert.Equal(RelayTestHost.Endpoint, reset.Endpoint);
        Assert.DoesNotContain(oldToken, shown.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(newToken, reset.ToString(), StringComparison.Ordinal);
    }
}
