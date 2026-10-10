namespace LiveCue.Hosting.Tests.Settings;

/// <summary>
/// Architecture 3.5: the data folder is <c>%LOCALAPPDATA%\LiveCueRemake</c> and never the previous
/// LiveCue's folder. Only paths are computed here; nothing is read, created or written.
/// </summary>
public sealed class DataFolderTests
{
    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void The_path_is_the_remake_folder()
    {
        Assert.Equal("LiveCueRemake", DataFolder.FolderName);
        Assert.Equal(Path.Combine(Local, "LiveCueRemake"), DataFolder.DefaultPath);
        Assert.Equal(DataFolder.DefaultPath, DataFolder.Resolve());
        Assert.Equal(DataFolder.DefaultPath, DataFolder.Resolve(null));
        Assert.Equal(DataFolder.DefaultPath, DataFolder.Resolve("  "));

        // Never the old application's folder, although its name is the beginning of the new one.
        Assert.NotEqual(Path.Combine(Local, "LiveCue"), DataFolder.DefaultPath);
        Assert.False(DataFolder.IsPreviousAppFolder(DataFolder.DefaultPath));
        Assert.False(DataFolder.IsPreviousAppFolder(Path.Combine(DataFolder.DefaultPath, "work")));
    }

    [Fact]
    public void An_override_is_used_as_an_absolute_path()
    {
        Assert.Equal(@"C:\LiveCue-tests\data", DataFolder.Resolve(@"C:\LiveCue-tests\data"));
        Assert.Equal(@"C:\LiveCue-tests\data", DataFolder.Resolve(@"C:\LiveCue-tests\other\..\data"));
        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "data"), DataFolder.Resolve("data"));
    }

    [Fact]
    public void The_previous_apps_folder_is_recognised_in_every_spelling()
    {
        var previous = Path.Combine(Local, "LiveCue");

        Assert.True(DataFolder.IsPreviousAppFolder(previous));
        Assert.True(DataFolder.IsPreviousAppFolder(previous + @"\"));
        Assert.True(DataFolder.IsPreviousAppFolder(previous.ToUpperInvariant()));
        Assert.True(DataFolder.IsPreviousAppFolder(Path.Combine(previous, "relay")));
        Assert.True(DataFolder.IsPreviousAppFolder(Path.Combine(Local, "LiveCueRemake", "..", "LiveCue")));

        Assert.False(DataFolder.IsPreviousAppFolder(Path.Combine(Local, "LiveCue2")));
        Assert.False(DataFolder.IsPreviousAppFolder(Local));
        Assert.False(DataFolder.IsPreviousAppFolder(@"C:\LiveCue-tests\data"));
        Assert.False(DataFolder.IsPreviousAppFolder(null));
        Assert.False(DataFolder.IsPreviousAppFolder(" "));
    }
}
