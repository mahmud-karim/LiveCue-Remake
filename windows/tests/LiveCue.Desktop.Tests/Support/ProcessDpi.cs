using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// Makes the test process DPI-unaware before WPF is loaded, so that every window a test creates is
/// laid out at 96 DPI: one layout unit is one pixel on any monitor, and positions and pictures do not
/// depend on the scale of the PC the tests run on. The app itself is per-monitor aware through its
/// manifest; a test host has no manifest, and WPF would otherwise take the system scale for it.
/// </summary>
internal static class ProcessDpi
{
    private static readonly IntPtr Unaware = new(-1);

    /// <summary>Runs when the test assembly is loaded, which is before any test touches a WPF type.</summary>
#pragma warning disable CA2255 // A test assembly is an application here: nothing else loads it.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        try
        {
            // Fails harmlessly when the awareness was already fixed; the layout tests then say so.
            _ = SetProcessDpiAwarenessContext(Unaware);
        }
        catch (EntryPointNotFoundException)
        {
            // Before Windows 10 version 1703. The tests that need exact pixels check the scale themselves.
        }
    }

#pragma warning disable SYSLIB1054 // One call in a test project does not justify compiling it with unsafe code.
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
#pragma warning restore SYSLIB1054
}
