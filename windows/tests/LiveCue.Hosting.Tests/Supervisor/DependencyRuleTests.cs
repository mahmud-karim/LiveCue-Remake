using LiveCue.Hosting.Tests.Support;

namespace LiveCue.Hosting.Tests.Supervisor;

/// <summary>Architecture 3.1: the hosting library has no user interface in it (test plan 2.3).</summary>
public sealed class DependencyRuleTests
{
    private static readonly string[] WindowAssemblies =
        ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml", "System.Windows.Forms"];

    [Fact]
    public void Hosting_references_no_wpf()
    {
        var references = typeof(IRelaySupervisor).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Contains("LiveCue.Relay", references);
        Assert.DoesNotContain(references, name => WindowAssemblies.Contains(name, StringComparer.Ordinal));
    }

    [Fact]
    public void The_console_host_references_no_wpf_either()
    {
        var references = typeof(LiveCue.Relay.Host.HostProgram).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Contains("LiveCue.Hosting", references);
        Assert.DoesNotContain(references, name => WindowAssemblies.Contains(name, StringComparer.Ordinal));
    }

    [Fact]
    public void Composing_the_hosting_library_does_no_io()
    {
        var files = new FakeHostFileSystem();
        var store = new InMemorySettingsStore();
        var processes = new LiveCue.Relay.Tests.Support.FakeProcessRunner();

        var services = HostingFactory.Create(
            new SupervisorOptions(@"C:\LiveCue-tests\data", "1.0.0"),
            new HostingOverrides { Files = files, Settings = store, Processes = processes });

        Assert.Equal(SupervisorState.Stopped, services.Supervisor.State);
        Assert.Equal(0, store.Loads);
        Assert.Empty(files.FilePaths);
        Assert.False(files.DirectoryExists(@"C:\LiveCue-tests\data"));
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public void Composing_with_the_real_file_system_creates_no_data_folder_and_no_settings_file()
    {
        using var temp = new LiveCue.TestSupport.TempFolder();
        var dataDirectory = temp.Combine("data");

        var services = HostingFactory.Create(new SupervisorOptions(dataDirectory, "1.0.0"));

        // Reading the settings of a folder that does not exist is the defaults, and still writes nothing.
        Assert.Equal(SettingsFileState.Missing, services.Settings.Load().State);
        Assert.True(services.Supervisor.Settings.SameValues(RelaySettings.Default));
        Assert.False(Directory.Exists(dataDirectory));
        Assert.Empty(temp.FileNames());
    }
}
