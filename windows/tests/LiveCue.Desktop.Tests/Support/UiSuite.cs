namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// The tests that create a window share one WPF application (<see cref="WpfApp"/>) and its one theme
/// slot, so they run one after another: every such class is in this collection.
/// </summary>
[CollectionDefinition(Name)]
#pragma warning disable CA1515 // xUnit finds a collection definition only on a public class.
public sealed class UiSuite
#pragma warning restore CA1515
{
    public const string Name = "Ui";
}
