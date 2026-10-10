using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The segmented control (design system 4.4): a few options of which exactly one is selected. It is a
/// list box, so arrow keys move the selection and the group is one tab stop. Its look is the
/// <c>Segmented</c> style.
/// </summary>
public sealed class SegmentedControl : ListBox
{
    public SegmentedControl()
    {
        SelectionMode = SelectionMode.Single;
    }
}
