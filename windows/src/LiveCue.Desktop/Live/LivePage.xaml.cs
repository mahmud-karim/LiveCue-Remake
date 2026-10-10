using System.Windows.Controls;

namespace LiveCue.Desktop.Live;

/// <summary>The Live page. Its data context is an <see cref="ILiveViewModel"/>.</summary>
public partial class LivePage : UserControl
{
    public LivePage()
    {
        InitializeComponent();
    }
}
