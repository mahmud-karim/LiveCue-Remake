namespace LiveCue.Relay.Tests.Contracts;

/// <summary>Architecture 3.1: the relay library stands alone.</summary>
public sealed class DependencyRuleTests
{
    [Fact]
    public void Relay_references_no_project()
    {
        var references = typeof(IRelay).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.DoesNotContain(references, name => name.StartsWith("LiveCue", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name =>
            name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" or "System.Windows.Forms");
    }

    [Fact]
    public void The_public_surface_is_the_small_one_of_the_architecture()
    {
        var publicTypes = typeof(IRelay).Assembly.GetExportedTypes()
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .Where(name => !name.StartsWith("LiveCue.Relay.Processes.", StringComparison.Ordinal))
            .ToList();

        // Everything else in the relay is internal and reaches the tests through InternalsVisibleTo.
        Assert.All(publicTypes, name => Assert.True(
            (name.StartsWith("LiveCue.Relay.", StringComparison.Ordinal) && name.Count(character => character == '.') == 2)
            || name is "LiveCue.Relay.Protocol.ProtocolConstants"
                or "LiveCue.Relay.Protocol.RelayCodes"
                or "LiveCue.Relay.Pairing.AdvertisedEndpoint",
            name + " is public."));
    }
}
