using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Desktop.Theming;

namespace LiveCue.Desktop.Tests.Theme;

/// <summary>
/// Every component style of W-design 5.3, used the way a page uses it: by its key, on the ground, in a
/// panel. The test proves that each style loads and draws under Daylight, under the contrast theme and
/// with transparency off, and that the rules of design system 3.5 hold for all of them. With the
/// environment variable <c>LIVECUE_TEST_SHOTS</c> set, the gallery is also written as PNG files, which
/// is how the look of the components is reviewed before a page uses them.
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class ComponentGalleryTests
{
    private const string GalleryXaml = """
        <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:ui="clr-namespace:LiveCue.Desktop.Controls;assembly=LiveCueRemake"
              TextElement.Foreground="{DynamicResource InkPrimaryBrush}"
              TextElement.FontFamily="{DynamicResource FontFamilyText}"
              TextElement.FontSize="{DynamicResource TypeBodySize}">
          <ui:BloomLayer Base="{DynamicResource GroundBaseBrush}"
                         Bloom1="{DynamicResource GroundBloom1Color}"
                         Bloom2="{DynamicResource GroundBloom2Color}"
                         Bloom3="{DynamicResource GroundBloom3Color}"
                         Bloom4="{DynamicResource GroundBloom4Color}" />
          <Grid Margin="16">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*" />
              <ColumnDefinition Width="16" />
              <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Column="0">
              <ContentControl Style="{DynamicResource Glass.Panel}">
                <StackPanel>
                  <TextBlock Text="Buttons" Style="{DynamicResource Type.Headline}" />
                  <WrapPanel Margin="0,12,0,0">
                    <Button Style="{DynamicResource Button.Primary}" Content="Show QR" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Primary}" Content="Save" ui:ButtonAssist.Icon="{DynamicResource Icon.Check}" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Primary}" Content="Saving" ui:ButtonAssist.IsBusy="True" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Primary}" Content="Save" IsEnabled="False" Margin="0,0,8,8" />
                  </WrapPanel>
                  <WrapPanel>
                    <Button Style="{DynamicResource Button.Secondary}" Content="Check again" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Secondary}" Content="Copy" ui:ButtonAssist.Icon="{DynamicResource Icon.Copy}" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Secondary}" Content="Checking" ui:ButtonAssist.IsBusy="True" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Secondary}" Content="Discard" IsEnabled="False" Margin="0,0,8,8" />
                  </WrapPanel>
                  <WrapPanel>
                    <Button Style="{DynamicResource Button.Quiet}" Content="Open settings" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Quiet}" Content="Clear activity" ui:ButtonAssist.Icon="{DynamicResource Icon.Trash}" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Quiet}" Content="Clear activity" IsEnabled="False" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Destructive}" Content="Reset pairing" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.DestructivePrimary}" Content="Reset pairing" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.DestructivePrimary}" Content="Resetting" ui:ButtonAssist.IsBusy="True" Margin="0,0,8,8" />
                  </WrapPanel>
                  <WrapPanel>
                    <Button Style="{DynamicResource Button.Primary.Session}" Content="Start relay" ui:ButtonAssist.Icon="{DynamicResource Icon.Play}" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Primary.Session}" Content="Start relay" ui:ButtonAssist.IsBusy="True" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Secondary.Session}" Content="Pause relay" ui:ButtonAssist.Icon="{DynamicResource Icon.Pause}" Margin="0,0,8,8" />
                    <Button Style="{DynamicResource Button.Secondary.Session}" Content="Stop relay" ui:ButtonAssist.Icon="{DynamicResource Icon.Stop}" ui:ButtonAssist.IconBrush="{DynamicResource StatusDangerBrush}" Margin="0,0,8,8" />
                  </WrapPanel>
                  <WrapPanel>
                    <Button Style="{DynamicResource Button.Icon}" ui:ButtonAssist.Icon="{DynamicResource Icon.Copy}" ToolTip="Copy" Margin="0,0,8,0" />
                    <Button Style="{DynamicResource Button.Icon}" ui:ButtonAssist.Icon="{DynamicResource Icon.Refresh}" ToolTip="Refresh" IsEnabled="False" Margin="0,0,8,0" />
                    <Button Style="{DynamicResource Button.Icon.Quiet}" ui:ButtonAssist.Icon="{DynamicResource Icon.Copy}" ToolTip="Copy" Margin="0,0,8,0" />
                    <Button Style="{DynamicResource Button.Icon.Quiet}" ui:ButtonAssist.Icon="{DynamicResource Icon.Close}" ToolTip="Close" Margin="0,0,8,0" />
                    <ui:Spinner Width="20" Height="20" Margin="12,0,8,0" VerticalAlignment="Center" />
                    <ui:Spinner Width="28" Height="28" Margin="0,0,8,0" VerticalAlignment="Center" />
                  </WrapPanel>
                </StackPanel>
              </ContentControl>

              <ContentControl Style="{DynamicResource Glass.Panel}" Margin="0,16,0,0">
                <StackPanel>
                  <TextBlock Text="Fields" Style="{DynamicResource Type.Headline}" />
                  <TextBlock Text="Relay port" Style="{DynamicResource Type.Caption}" FontWeight="SemiBold" Foreground="{DynamicResource InkSecondaryBrush}" Margin="0,12,0,6" />
                  <TextBox Style="{DynamicResource Field.Text}" Text="47841" />
                  <TextBlock Text="Codex path" Style="{DynamicResource Type.Caption}" FontWeight="SemiBold" Foreground="{DynamicResource InkSecondaryBrush}" Margin="0,12,0,6" />
                  <TextBox Style="{DynamicResource Field.Text}" Tag="Found automatically" />
                  <TextBlock Text="Read-only and disabled" Style="{DynamicResource Type.Caption}" FontWeight="SemiBold" Foreground="{DynamicResource InkSecondaryBrush}" Margin="0,12,0,6" />
                  <TextBox Style="{DynamicResource Field.Text}" Text="https://pc.example.test:11443/" IsReadOnly="True" FontFamily="{DynamicResource FontFamilyMono}" />
                  <TextBox Style="{DynamicResource Field.Text}" Text="10000" IsEnabled="False" Margin="0,8,0,0" />
                  <TextBlock Text="LiveCue will run:" Style="{DynamicResource Type.Caption}" FontWeight="SemiBold" Foreground="{DynamicResource InkSecondaryBrush}" Margin="0,12,0,6" />
                  <ContentControl Style="{DynamicResource Field.ReadOnly}">
                    <TextBlock Text="tailscale serve --bg --https=11443 http://127.0.0.1:47841" Style="{DynamicResource Type.Mono}" TextTrimming="CharacterEllipsis" />
                  </ContentControl>
                  <TextBlock Text="Connection" Style="{DynamicResource Type.Caption}" FontWeight="SemiBold" Foreground="{DynamicResource InkSecondaryBrush}" Margin="0,12,0,6" />
                  <ui:SegmentedControl x:Name="Mode" Style="{DynamicResource Segmented}" SelectedIndex="0">
                    <ListBoxItem Content="Private tailnet" />
                    <ListBoxItem Content="Public internet" />
                  </ui:SegmentedControl>
                  <ui:SegmentedControl Style="{DynamicResource Segmented}" SelectedIndex="1" IsEnabled="False" Margin="0,8,0,0">
                    <ListBoxItem Content="Private tailnet" />
                    <ListBoxItem Content="Public internet" />
                  </ui:SegmentedControl>
                  <CheckBox Content="Hide this window from screen capture" IsChecked="True" Margin="0,8,0,0" />
                  <CheckBox Content="Hide this window from screen capture" IsChecked="False" />
                  <CheckBox Content="Hide this window from screen capture" IsChecked="True" IsEnabled="False" />
                </StackPanel>
              </ContentControl>

              <ContentControl Style="{DynamicResource Glass.PanelCompact}" Margin="0,16,0,0">
                <StackPanel Orientation="Horizontal">
                  <StackPanel>
                    <TextBlock Text="Relay" Style="{DynamicResource Type.Micro}" Foreground="{DynamicResource InkSecondaryBrush}" />
                    <ui:StatusIndicatorView x:Name="IndicatorReady" Style="{DynamicResource Indicator}" Margin="0,4,0,0" />
                  </StackPanel>
                  <StackPanel Margin="32,0,0,0">
                    <TextBlock Text="iPhone" Style="{DynamicResource Type.Micro}" Foreground="{DynamicResource InkSecondaryBrush}" />
                    <ui:StatusIndicatorView x:Name="IndicatorIdle" Style="{DynamicResource Indicator}" Margin="0,4,0,0" />
                  </StackPanel>
                  <StackPanel Margin="32,0,0,0">
                    <TextBlock Text="Compact panel" Style="{DynamicResource Type.Micro}" Foreground="{DynamicResource InkSecondaryBrush}" />
                    <TextBlock Text="https://pc.example.test:11443/" Style="{DynamicResource Type.Mono}" Margin="0,4,0,0" />
                  </StackPanel>
                </StackPanel>
              </ContentControl>

              <ContentControl Style="{DynamicResource Glass.Panel}" Margin="0,16,0,0" Height="300">
                <ContentControl Style="{DynamicResource EmptyState}">
                  <StackPanel>
                    <ContentControl Style="{DynamicResource EmptyState.Tile}">
                      <ui:Icon Glyph="{DynamicResource Icon.Activity}" Width="28" Height="28" />
                    </ContentControl>
                    <TextBlock Text="No activity yet" Style="{DynamicResource Type.Title}" Margin="0,16,0,0" TextAlignment="Center" />
                    <TextBlock Text="Requests from your iPhone and relay messages will appear here."
                               Style="{DynamicResource Type.Body}" Foreground="{DynamicResource InkSecondaryBrush}"
                               TextWrapping="Wrap" TextAlignment="Center" Margin="0,6,0,0" />
                  </StackPanel>
                </ContentControl>
              </ContentControl>
            </StackPanel>

            <StackPanel Grid.Column="2">
              <ContentControl Style="{DynamicResource Glass.Panel}">
                <StackPanel>
                  <TextBlock Text="Status" Style="{DynamicResource Type.Headline}" />
                  <WrapPanel x:Name="Indicators" Margin="0,12,0,0" />
                  <WrapPanel Margin="0,12,0,0">
                    <ui:StatusChip Style="{DynamicResource Chip}" Kind="Neutral" Text="Not paired" Margin="0,0,8,8" />
                    <ui:StatusChip Style="{DynamicResource Chip}" Kind="Live" Text="Listening" Margin="0,0,8,8" />
                    <ui:StatusChip Style="{DynamicResource Chip}" Kind="Success" Text="Connected" Margin="0,0,8,8" />
                    <ui:StatusChip Style="{DynamicResource Chip}" Kind="Warning" Text="Paused" Margin="0,0,8,8" />
                    <ui:StatusChip Style="{DynamicResource Chip}" Kind="Danger" Text="Failed" Margin="0,0,8,8" />
                  </WrapPanel>
                  <ui:Banner Style="{DynamicResource Banner}" Tone="Info" Title="Restart to apply" Body="The relay runs with the settings of its last start." ActionLabel="Restart relay" Margin="0,4,0,0" />
                  <ui:Banner Style="{DynamicResource Banner}" Tone="Success" Body="Connection set up. Your iPhone can pair now." Margin="0,8,0,0" />
                  <ui:Banner Style="{DynamicResource Banner}" Tone="Warning" Title="Public mode" Body="Anyone on the internet can reach this address. Only an iPhone with the pairing token gets past it." Margin="0,8,0,0" />
                  <ui:Banner Style="{DynamicResource Banner}" Tone="Danger" Body="The pairing could not be reset. Check that LiveCue can write its data folder, then retry." Margin="0,8,0,0" />
                </StackPanel>
              </ContentControl>

              <ContentControl Style="{DynamicResource Glass.Panel}" Padding="{DynamicResource InsetPanelRows}" Margin="0,16,0,0">
                <ItemsControl x:Name="Checks" Focusable="False" Margin="12,0">
                  <ui:CheckRow Style="{DynamicResource Row.Check}" State="Passed" Title="Codex CLI found" Subtitle="C:\Tools\codex.exe" />
                  <ui:CheckRow Style="{DynamicResource Row.Check}" State="Failed" Title="Codex is installed but not signed in" Subtitle="Open Codex and sign in with ChatGPT, then choose Check again." />
                  <ui:CheckRow Style="{DynamicResource Row.Check}" State="NotChecked" Title="Tailscale is online" />
                </ItemsControl>
              </ContentControl>

              <ContentControl Style="{DynamicResource Glass.Panel}" Padding="{DynamicResource InsetPanelRows}" Margin="0,16,0,0">
                <StackPanel>
                  <ui:ListRow Style="{DynamicResource Row.List}" Glyph="{DynamicResource Icon.Link}" Title="Private tailnet" Subtitle="https://pc.example.test:11443/" ShowSeparator="False" />
                  <ui:ListRow Style="{DynamicResource Row.List}" Glyph="{DynamicResource Icon.Folder}" Title="Open the notices" IsInteractive="True" ShowChevron="True" />
                </StackPanel>
              </ContentControl>

              <Grid Margin="0,16,0,0">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="16" />
                  <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <ContentControl Style="{DynamicResource Glass.Panel}">
                  <StackPanel>
                    <TextBlock Text="Card" Style="{DynamicResource Type.Headline}" />
                    <ContentControl Style="{DynamicResource Glass.Card}" Padding="{DynamicResource InsetCard}" Margin="0,12,0,0">
                      <StackPanel>
                        <TextBlock Text="From iPhone" Style="{DynamicResource Type.Caption}" FontWeight="SemiBold" />
                        <TextBlock Text="Your next question will appear here." Style="{DynamicResource Type.Body}" Foreground="{DynamicResource InkSecondaryBrush}" TextWrapping="Wrap" Margin="0,8,0,0" />
                        <Border Style="{DynamicResource Divider}" Margin="0,12" />
                        <TextBlock Text="09:14:02 Relay started. Waiting for your iPhone." Style="{DynamicResource Log.Text}" />
                      </StackPanel>
                    </ContentControl>
                  </StackPanel>
                </ContentControl>
                <ContentControl Grid.Column="2" Style="{DynamicResource Glass.Panel}">
                  <StackPanel>
                    <TextBlock Text="Prominent card" Style="{DynamicResource Type.Headline}" />
                    <ContentControl Style="{DynamicResource Glass.CardProminent}" Padding="{DynamicResource InsetCard}" Margin="0,12,0,0">
                      <StackPanel>
                        <TextBlock Text="Assistant" Style="{DynamicResource Type.Caption}" FontWeight="SemiBold" Foreground="{DynamicResource AccentTextBrush}" />
                        <TextBlock Text="Ready when you are." Style="{DynamicResource Type.Cue}" TextWrapping="Wrap" Margin="0,8,0,0" />
                      </StackPanel>
                    </ContentControl>
                  </StackPanel>
                </ContentControl>
              </Grid>

              <Grid Margin="0,16,0,0" Height="150">
                <Border Style="{DynamicResource Dialog.Scrim}" CornerRadius="30" />
                <ContentControl Style="{DynamicResource Toast}" Margin="0,0,0,24">
                  <TextBlock Text="Copied" />
                </ContentControl>
              </Grid>
            </StackPanel>
          </Grid>
        </Grid>
        """;

    [Fact]
    public void Every_component_draws_with_its_style() => WpfApp.Run(() =>
    {
        var themes = new ThemeManager(Application.Current.Resources);
        var gallery = BuildGallery();
        var window = new Window
        {
            Content = gallery,
            Width = 1240,
            Height = 1640,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        try
        {
            WpfApp.ShowOffScreen(window);
            WpfApp.Settle(TimeSpan.FromMilliseconds(100));

            // Daylight: every control has taken a template and a size, text is crisp, and every shadow is
            // on a caster layer that holds no text (the two-layer glass of design system 3.5).
            var controls = WpfApp.Descendants(gallery).OfType<Control>().Where(control => control.IsVisible).ToList();
            Assert.All(controls, control =>
            {
                Assert.True(control.Template is not null || control is Spinner, control.GetType().Name + " has no template.");
                Assert.True(control.ActualWidth > 0 && control.ActualHeight > 0, control.GetType().Name + " has no size.");
            });
            Assert.True(VisualChecks.RequireCrispText(gallery) > 40);
            var casters = VisualChecks.RequireEffectsOnlyOnCasters(gallery);
            Assert.True(casters > 20, $"Only {casters} shadow casters.");
            WpfApp.Render(gallery, "gallery-daylight.png");

            // Sizes the design system fixes.
            Assert.All(Buttons(gallery, "Button.Primary"), button => Assert.Equal(40d, button.ActualHeight));
            Assert.All(Buttons(gallery, "Button.Secondary"), button => Assert.Equal(40d, button.ActualHeight));
            Assert.All(Buttons(gallery, "Button.Primary.Session"), button => Assert.Equal(48d, button.ActualHeight));
            Assert.All(Buttons(gallery, "Button.Secondary.Session"), button => Assert.Equal(48d, button.ActualHeight));
            Assert.All(Buttons(gallery, "Button.Icon"), button => Assert.Equal((40d, 40d), (button.ActualWidth, button.ActualHeight)));
            Assert.All(WpfApp.Descendants(gallery).OfType<TextBox>(), field => Assert.Equal(44d, field.ActualHeight));
            Assert.All(WpfApp.Descendants(gallery).OfType<SegmentedControl>(), segmented => Assert.Equal(44d, segmented.ActualHeight));
            Assert.All(WpfApp.Descendants(gallery).OfType<CheckBox>(), toggle => Assert.True(toggle.ActualHeight >= 44));
            Assert.All(WpfApp.Descendants(gallery).OfType<StatusChip>(), chip => Assert.Equal(28d, chip.ActualHeight));

            // A busy button keeps its label and its full opacity, and ignores the pointer.
            var busy = Buttons(gallery, "Button.Primary").Single(ButtonAssist.GetIsBusy);
            Assert.Equal(1d, busy.Opacity);
            Assert.Single(WpfApp.Descendants(busy).OfType<Spinner>(), spinner => spinner.IsVisible);
            Assert.Contains("Saving", WpfApp.Descendants(busy).OfType<TextBlock>().Select(text => text.Text));

            // Check rows are separated by a hairline, except the first one a list makes.
            var checks = ((ItemsControl)gallery.FindName("Checks")).Items.Cast<CheckRow>().ToList();
            Assert.Equal([true, false, false], checks.Select(row => row.IsFirstInList));
            Assert.Equal(
                [Visibility.Collapsed, Visibility.Visible, Visibility.Visible],
                checks.Select(row => ((FrameworkElement)row.Template.FindName("Separator", row)).Visibility));
            Assert.All(checks, row => Assert.True(row.ActualHeight >= 48));

            // A text field starts its text where its placeholder stands: 14 of padding from the outline.
            var fields = WpfApp.Descendants(gallery).OfType<TextBox>().ToList();
            var typed = WpfApp.Descendants(fields[0]).OfType<ScrollViewer>().Single();
            var placeholder = (FrameworkElement)fields[1].Template.FindName("Placeholder", fields[1]);
            Assert.Equal(14d, Left(typed, fields[0]) + typed.Padding.Left);
            Assert.Equal(14d, Left(placeholder, fields[1]));
            Assert.Equal(Visibility.Visible, placeholder.Visibility);

            // A banner shows what its model says, and takes no room without one.
            var banner = WpfApp.Descendants(gallery).OfType<Banner>().First();
            var said = (banner.Tone, banner.Title, banner.Body, banner.ActionLabel);
            Assert.True(banner.IsVisible);
            banner.Model = new BannerModel(BannerTone.Danger, null, "The pairing could not be reset.");
            Assert.Equal((BannerTone.Danger, null, "The pairing could not be reset.", null), (banner.Tone, banner.Title, banner.Body, banner.ActionLabel));
            Assert.Equal(Visibility.Visible, banner.Visibility);
            banner.Model = null;
            Assert.Equal(Visibility.Collapsed, banner.Visibility);
            banner.Model = new BannerModel(said.Tone, said.Title, said.Body, said.ActionLabel);
            Assert.Equal(Visibility.Visible, banner.Visibility);
            WpfApp.Settle();

            // The segmented thumb sits under the selected segment, and moves with the selection.
            var mode = (SegmentedControl)gallery.FindName("Mode");
            var thumb = (FrameworkElement)mode.Template.FindName("PART_Thumb", mode);
            var first = (ListBoxItem)mode.ItemContainerGenerator.ContainerFromIndex(0);
            var second = (ListBoxItem)mode.ItemContainerGenerator.ContainerFromIndex(1);
            Assert.Equal(Visibility.Visible, thumb.Visibility);
            Assert.Equal(first.ActualWidth, second.ActualWidth);
            Assert.Equal(Left(first, mode), Left(thumb, mode));
            Assert.Equal(first.ActualWidth, thumb.ActualWidth);
            Assert.Equal(36d, thumb.ActualHeight);
            mode.SelectedIndex = 1;
            WpfApp.Settle(TimeSpan.FromMilliseconds(50));
            Assert.Equal(Left(second, mode), Left(thumb, mode));
            mode.SelectedIndex = 0;
            WpfApp.Settle(TimeSpan.FromMilliseconds(50));

            // The contrast theme: the same tree with no shadow, bloom or tint anywhere.
            themes.Apply(AppTheme.HighContrast);
            WpfApp.Settle(TimeSpan.FromMilliseconds(50));
            Assert.Equal(0, VisualChecks.CountEffects(gallery));
            Assert.True(VisualChecks.RequireCrispText(gallery) > 40);
            WpfApp.Render(gallery, "gallery-high-contrast.png");

            // Transparency off: solid surfaces, no sheen, shadows kept.
            themes.Apply(AppTheme.Daylight);
            themes.ReduceTransparency(true);
            WpfApp.Settle(TimeSpan.FromMilliseconds(50));
            Assert.Equal(255, ((SolidColorBrush)gallery.FindResource("SurfacePanelBrush")).Color.A);
            Assert.True(VisualChecks.RequireEffectsOnlyOnCasters(gallery) > 20);
            WpfApp.Render(gallery, "gallery-solid.png");

            // The Windows text size at 150%: every text grows and nothing is cut off its control.
            themes.ReduceTransparency(false);
            themes.ScaleText(1.5);
            WpfApp.Settle(TimeSpan.FromMilliseconds(50));
            Assert.Equal(21d, (double)gallery.FindResource("TypeBodySize"));
            Assert.All(Buttons(gallery, "Button.Primary"), button => Assert.True(button.ActualHeight >= 40));
            WpfApp.Render(gallery, "gallery-text-150.png");
        }
        finally
        {
            themes.ScaleText(1);
            themes.ReduceTransparency(false);
            themes.Apply(AppTheme.Daylight);
            window.Close();
        }
    });

    private static Grid BuildGallery()
    {
        var gallery = (Grid)XamlReader.Parse(GalleryXaml);

        // One indicator per mark and tone the status texts use (W-design 5.4).
        var indicators = (Panel)gallery.FindName("Indicators");
        StatusIndicator[] shown =
        [
            new(StatusMark.Ring, StatusTone.Offline, "Stopped"),
            new(StatusMark.Spinner, StatusTone.Live, "Checking Codex and Tailscale…"),
            new(StatusMark.Dot, StatusTone.Success, "Ready"),
            new(StatusMark.Pause, StatusTone.Warning, "Paused"),
            new(StatusMark.Spinner, StatusTone.Live, "Thinking…"),
            new(StatusMark.Alert, StatusTone.Danger, "Request failed. Ready to retry."),
            new(StatusMark.Dot, StatusTone.Warning, "Relay stopped. Click Start relay to reconnect."),
            new(StatusMark.Check, StatusTone.Success, "Verified"),
        ];
        foreach (var indicator in shown)
        {
            indicators.Children.Add(new StatusIndicatorView { Indicator = indicator, Margin = new Thickness(0, 0, 24, 8) });
        }

        ((StatusIndicatorView)gallery.FindName("IndicatorReady")).Indicator = shown[2];
        ((StatusIndicatorView)gallery.FindName("IndicatorIdle")).Indicator =
            new StatusIndicator(StatusMark.Dot, StatusTone.Warning, "iPhone idle");
        return gallery;
    }

    private static List<Button> Buttons(FrameworkElement root, string styleKey)
    {
        var style = (Style)root.FindResource(styleKey);
        return WpfApp.Descendants(root).OfType<Button>().Where(button => ReferenceEquals(button.Style, style)).ToList();
    }

    private static double Left(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).Transform(default).X;
}
