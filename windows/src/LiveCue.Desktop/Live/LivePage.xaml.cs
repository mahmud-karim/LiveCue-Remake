using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace LiveCue.Desktop.Live;

/// <summary>
/// The Live page. Its data context is an <see cref="ILiveViewModel"/>.
///
/// The view does three things a binding cannot: it keeps the newest line of the transcript in view, it
/// gives the button inside the connection banner its automation id, and it decides whether the page
/// fills the window or scrolls.
/// </summary>
public partial class LivePage : UserControl
{
    /// <summary>The least height of the prerequisite list's panel, when it stands in place of the two panels.</summary>
    private const double PanelFloor = 240;

    private INotifyPropertyChanged? _viewModel;

    public LivePage()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Follow(IsLoaded ? DataContext as INotifyPropertyChanged : null);
        Loaded += (_, _) =>
        {
            Follow(DataContext as INotifyPropertyChanged);
            NameConnectionButton();
            ShowNewestTranscript();

            // After every layout pass: cheap, and it settles at once because the answer does not move.
            LayoutUpdated += OnLayoutUpdated;
            FitPage();
        };

        // The view model outlives the view (the page is built again each time it is shown), and so
        // does the layout that raises LayoutUpdated: a view that has left lets go of both.
        Unloaded += (_, _) =>
        {
            Follow(null);
            LayoutUpdated -= OnLayoutUpdated;
        };
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => FitPage();

    /// <summary>
    /// The content is as high as the window gives it, so the two cards take what is left and their
    /// text scrolls inside them (screens section 7). The cards have a floor; when the strip above them
    /// has grown so far that the floor no longer fits, the content gets the height it needs and the
    /// page scrolls. A scrolling panel cannot decide this by itself: it would let the cards grow to the
    /// length of their text.
    /// </summary>
    private void FitPage()
    {
        if (!IsLoaded || PageScroller.ActualHeight <= 0)
        {
            return;
        }

        var margin = PageContent.Margin;
        var given = Math.Max(0, PageScroller.ActualHeight - margin.Top - margin.Bottom);
        var lower = PrerequisitePanel.IsVisible
            ? PanelFloor
            : Math.Max(LeastHeight(TranscriptPanel, TranscriptCard), LeastHeight(ReplyPanel, ReplyCard));
        var height = Math.Ceiling(Math.Max(given, StatusStrip.DesiredSize.Height + lower));

        if (double.IsNaN(PageContent.Height) || Math.Abs(PageContent.Height - height) > 0.5)
        {
            PageContent.Height = height;
        }
    }

    /// <summary>What a panel needs when its card is at its floor: its inset, its lines above and below the card, and the card.</summary>
    private static double LeastHeight(ContentControl panel, ContentControl card)
    {
        var height = panel.Padding.Top + panel.Padding.Bottom + card.MinHeight + card.Margin.Top + card.Margin.Bottom;
        if (panel.Content is Panel lines)
        {
            foreach (UIElement line in lines.Children)
            {
                if (!ReferenceEquals(line, card))
                {
                    height += line.DesiredSize.Height;
                }
            }
        }

        return height;
    }

    private void Follow(INotifyPropertyChanged? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(ILiveViewModel.TranscriptText) or nameof(ILiveViewModel.TranscriptSecondaryText):
                ShowNewestTranscript();
                break;

            case nameof(ILiveViewModel.ReplyText):
                // A new reply is read from its first line.
                ReplyScroller.ScrollToHome();
                break;

            case nameof(ILiveViewModel.ConnectionBanner):
                NameConnectionButton();
                break;

            default:
                break;
        }
    }

    /// <summary>New text is scrolled into view (screens 2.7), once the bindings have brought it in.</summary>
    private void ShowNewestTranscript() =>
        _ = Dispatcher.InvokeAsync(TranscriptScroller.ScrollToEnd, DispatcherPriority.Loaded);

    /// <summary>
    /// The banner draws its own button; the page says which button it is (screens section 9,
    /// LiveSetUpConnectionButton). The banner builds its template when it is first shown.
    /// </summary>
    private void NameConnectionButton() => _ = Dispatcher.InvokeAsync(
        () =>
        {
            ConnectionBannerView.ApplyTemplate();
            if (FindButton(ConnectionBannerView) is { } button
                && AutomationProperties.GetAutomationId(button) != AutomationIds.LiveSetUpConnectionButton)
            {
                AutomationProperties.SetAutomationId(button, AutomationIds.LiveSetUpConnectionButton);
            }
        },
        DispatcherPriority.Loaded);

    private static ButtonBase? FindButton(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ButtonBase button)
            {
                return button;
            }

            if (FindButton(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
