using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The segmented control (design system 4.4): two to four options of which exactly one is selected. It
/// is a list box, so arrow keys move the selection and the group is one tab stop. Its look is the
/// <c>Segmented</c> style: a trough, equal-width segments, and one raised thumb under the selected
/// segment that slides to the next over motion.base (at once when Windows has animations off).
///
/// The thumb is a sibling under the labels, not their parent, so the labels never move with a
/// transform and stay crisp.
/// </summary>
[TemplatePart(Name = ThumbPart, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TrackPart, Type = typeof(FrameworkElement))]
public sealed class SegmentedControl : ListBox
{
    private const string ThumbPart = "PART_Thumb";
    private const string TrackPart = "PART_Track";

    private readonly TranslateTransform _travel = new();
    private FrameworkElement? _thumb;
    private FrameworkElement? _track;
    private int _placedAt = -1;

    public SegmentedControl()
    {
        SelectionMode = SelectionMode.Single;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_track is not null)
        {
            _track.SizeChanged -= OnTrackSizeChanged;
        }

        _thumb = GetTemplateChild(ThumbPart) as FrameworkElement;
        _track = GetTemplateChild(TrackPart) as FrameworkElement;
        _placedAt = -1;
        if (_thumb is not null)
        {
            _thumb.HorizontalAlignment = HorizontalAlignment.Left;
            _thumb.RenderTransform = _travel;
        }

        if (_track is not null)
        {
            _track.SizeChanged += OnTrackSizeChanged;
        }

        PlaceThumb(animate: false);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // One option is always selected: a selection that was cleared by the user is put back.
        if (SelectedIndex < 0 && e.RemovedItems.Count > 0 && Items.Contains(e.RemovedItems[0]))
        {
            SetCurrentValue(SelectedItemProperty, e.RemovedItems[0]);
            return;
        }

        base.OnSelectionChanged(e);
        PlaceThumb(animate: true);
    }

    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        PlaceThumb(animate: false);
    }

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e) => PlaceThumb(animate: false);

    /// <summary>Puts the thumb under the selected segment: every segment is one count-th of the track.</summary>
    private void PlaceThumb(bool animate)
    {
        if (_thumb is null || _track is null)
        {
            return;
        }

        var index = SelectedIndex;
        var count = Items.Count;
        if (index < 0 || count == 0 || _track.ActualWidth <= 0)
        {
            _thumb.Visibility = Visibility.Hidden;
            _placedAt = -1;
            return;
        }

        var segment = _track.ActualWidth / count;
        var target = index * segment;
        _thumb.Width = segment;
        _thumb.Visibility = Visibility.Visible;

        // It travels only from one segment to another; a first placement or a resize is not a movement.
        var travels = animate && _placedAt >= 0 && _placedAt != index && Motion.IsEnabledFor(this);
        _placedAt = index;
        Motion.Run(_travel, TranslateTransform.XProperty, _travel.X, target, Motion.Base, Motion.Standard, travels);
    }
}
