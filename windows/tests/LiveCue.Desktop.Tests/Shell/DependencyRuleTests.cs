using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Shell;

/// <summary>
/// Architecture 3.1 and 3.4: the desktop can show a pairing matrix and can never obtain the pairing
/// payload. The proof is a scan of the desktop assembly's own metadata, not of its source.
/// </summary>
public sealed class DependencyRuleTests
{
    [Fact]
    public void Desktop_never_references_pairing_export()
    {
        using var file = File.OpenRead(typeof(App).Assembly.Location);
        using var portable = new PEReader(file);
        var metadata = portable.GetMetadataReader();

        var referencedTypes = metadata.TypeReferences
            .Select(handle => metadata.GetString(metadata.GetTypeReference(handle).Name))
            .ToHashSet(StringComparer.Ordinal);
        var referencedMembers = metadata.MemberReferences
            .Select(handle => metadata.GetString(metadata.GetMemberReference(handle).Name))
            .ToHashSet(StringComparer.Ordinal);

        // The two ways to the payload text: the relay object's export, and the stored pairing's export.
        Assert.DoesNotContain(nameof(IPairingExport), referencedTypes);
        Assert.DoesNotContain(nameof(StoredPairingExport), referencedTypes);
        Assert.DoesNotContain(nameof(IPairingExport.GetPairingPayloadAsync), referencedMembers);
        Assert.DoesNotContain(nameof(StoredPairingExport.ReadPayloadAsync), referencedMembers);

        // The scan sees what the assembly does use, so an empty result above means something.
        Assert.Contains(nameof(IRelay), referencedTypes);
        Assert.Contains(nameof(PairingQr), referencedTypes);
        Assert.Contains(nameof(IRelay.ShowPairingAsync), referencedMembers);
    }

    [Fact]
    public void The_desktop_assembly_is_named_for_the_remake_and_reaches_the_relay_through_hosting()
    {
        var assembly = typeof(App).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Equal("LiveCueRemake", assembly.GetName().Name);
        Assert.Contains("LiveCue.Hosting", references);
    }
}
