namespace LiveCue.Hosting.Tests.Supervisor;

/// <summary>Architecture 3.1: the hosting library has no user interface in it.</summary>
public sealed class DependencyRuleTests
{
    [Fact]
    public void Hosting_references_no_wpf()
    {
        var references = typeof(IRelaySupervisor).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Contains("LiveCue.Relay", references);
        Assert.DoesNotContain(references, name =>
            name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" or "System.Windows.Forms");
    }

    [Fact]
    public void Composing_the_hosting_library_does_no_io()
    {
        var files = new Support.FakeHostFileSystem();
        var store = new Support.InMemorySettingsStore();

        var services = HostingFactory.Create(
            new SupervisorOptions(@"C:\LiveCue-tests\data", "1.0.0"),
            new HostingOverrides { Files = files, Settings = store });

        Assert.Equal(SupervisorState.Stopped, services.Supervisor.State);
        Assert.Equal(0, store.Loads);
        Assert.Empty(files.FilePaths);
        Assert.False(files.DirectoryExists(@"C:\LiveCue-tests\data"));
    }
}
