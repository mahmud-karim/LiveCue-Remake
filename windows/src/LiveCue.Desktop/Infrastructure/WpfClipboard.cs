using System.Runtime.InteropServices;
using System.Windows;

namespace LiveCue.Desktop.Infrastructure;

/// <summary>The Windows clipboard. Another program may hold it open; that is "unavailable", not an error.</summary>
public sealed class WpfClipboard : IClipboard
{
    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetDataObject(text, copy: true);
            return true;
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            return false;
        }
    }
}
