using System.Globalization;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Settings;

/// <summary>
/// The editable copy of the settings behind the Settings page (W-design 5.8): the fields as they are
/// typed. Nothing in it is saved until the page's "Save"; two forms are equal when every field is, so
/// "has unsaved changes" is a comparison with the form of the saved settings.
///
/// Only what the page edits is here. Everything else of <see cref="RelaySettings"/> (the ledger of
/// mappings, the lab port, the recorded Codex version) is carried over from the saved settings when a
/// form is turned back into settings.
/// </summary>
/// <param name="PublicHttpsPort">443, 8443 or 10000: chosen, not typed.</param>
/// <param name="CodexPathText">Blank means automatic.</param>
/// <param name="LabFolderText">Blank means automatic.</param>
internal sealed record SettingsForm(
    ConnectionMode Mode,
    string RelayPortText,
    string PrivateHttpsPortText,
    int PublicHttpsPort,
    string PublicPathPrefix,
    string CodexPathText,
    string LabFolderText,
    bool HideFromScreenCapture)
{
    /// <summary>What a port field that holds no number is validated as: outside every allowed range.</summary>
    public const int NotAPort = 0;

    /// <summary>The form that shows <paramref name="settings"/>.</summary>
    public static SettingsForm From(RelaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SettingsForm(
            settings.ConnectionMode,
            settings.RelayPort.ToString(CultureInfo.InvariantCulture),
            settings.PrivateHttpsPort.ToString(CultureInfo.InvariantCulture),
            settings.PublicHttpsPort,
            settings.PublicPathPrefix,
            settings.CodexExecutablePath ?? string.Empty,
            settings.AsrLabFolder ?? string.Empty,
            settings.HideFromScreenCapture);
    }

    /// <summary>
    /// The settings this form asks for, on top of <paramref name="saved"/>. A port field without a
    /// number becomes <see cref="NotAPort"/>, which the settings rules report as out of range.
    /// </summary>
    public RelaySettings ApplyTo(RelaySettings saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return saved with
        {
            ConnectionMode = Mode,
            RelayPort = ParsePort(RelayPortText),
            PrivateHttpsPort = ParsePort(PrivateHttpsPortText),
            PublicHttpsPort = PublicHttpsPort,
            PublicPathPrefix = PublicPathPrefix.Trim(),
            CodexExecutablePath = ParsePath(CodexPathText),
            AsrLabFolder = ParsePath(LabFolderText),
            HideFromScreenCapture = HideFromScreenCapture,
        };
    }

    /// <summary>Digits only, as a port is written; anything else is <see cref="NotAPort"/>.</summary>
    public static int ParsePort(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : NotAPort;

    /// <summary>Blank means automatic (null). The quotes that "Copy as path" puts around a path are dropped.</summary>
    public static string? ParsePath(string? text)
    {
        var path = text?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
