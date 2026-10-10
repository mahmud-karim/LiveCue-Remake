using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace LiveCue.Desktop.Infrastructure;

/// <summary>
/// Opens something outside the app with the Windows shell: an https page in the default browser, or an
/// existing file with its default program. Nothing else is ever opened.
/// </summary>
public sealed class ShellLauncher : IExternalLauncher
{
    public bool OpenUrl(Uri url) =>
        url is { IsAbsoluteUri: true } && url.Scheme == Uri.UriSchemeHttps && Open(url.AbsoluteUri);

    public bool OpenFile(string path) =>
        !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && File.Exists(path) && Open(path);

    private static bool Open(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
