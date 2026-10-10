namespace LiveCue.Desktop;

/// <summary>The mark of an inline status indicator (DS 4.22).</summary>
public enum StatusMark { Ring, Dot, Spinner, Pause, Alert, Check }

/// <summary>The status colour token of DS 2.7.</summary>
public enum StatusTone { Offline, Live, Success, Warning, Danger }

/// <summary>One inline indicator: mark, colour, visible text and an optional tooltip.</summary>
public sealed record StatusIndicator(StatusMark Mark, StatusTone Tone, string Text, string? ToolTip = null);

public enum BannerTone { Info, Success, Warning, Danger }

/// <summary>A banner (DS 4.11). ActionLabel is the label of its one quiet button, or null.</summary>
public sealed record BannerModel(BannerTone Tone, string? Title, string Body, string? ActionLabel = null);

public enum AppPage { Live, Pairing, Activity, Settings }
