using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// What has to be true of the test process before the first test touches WPF. It runs once, when the
/// test assembly is loaded, so no test can come first and nothing depends on the order of the tests.
///
/// 96 DPI. The app is per-monitor aware through its manifest; a test host has no manifest, and WPF
/// would lay every window out at the scale of the PC the tests happen to run on. A DPI-unaware process
/// has one layout unit per pixel everywhere, so positions and pictures are the same on any PC.
///
/// The app's assembly as the resource assembly. A pack URI without an assembly name
/// ("pack://application:,,,/Assets/…", as the window's icon is written) is looked up in the
/// application's resource assembly, which WPF takes to be the entry assembly: the test host. WPF
/// remembers the first answer for the life of the process.
/// </summary>
internal static class TestProcess
{
    private static readonly IntPtr DpiUnaware = new(-1);

#pragma warning disable CA2255 // A test assembly is an application here: nothing else loads it.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        try
        {
            // Fails harmlessly when the awareness was already fixed; the layout tests then say so.
            _ = SetProcessDpiAwarenessContext(DpiUnaware);
        }
        catch (EntryPointNotFoundException)
        {
            // Before Windows 10 version 1703. The tests that need exact pixels check the scale themselves.
        }

        UseTheAppsAssemblyForResources();
    }

    private static void UseTheAppsAssemblyForResources()
    {
        var assembly = typeof(App).Assembly;
        try
        {
            Application.ResourceAssembly = assembly;
        }
        catch (InvalidOperationException)
        {
            // WPF fixes the property for a process that has an entry assembly, as a test host has. The
            // two places that remember it are set directly instead.
            typeof(Application)
                .GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic)?
                .SetValue(null, assembly);
            typeof(System.Windows.Navigation.BaseUriHelper)
                .GetProperty("ResourceAssembly", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?
                .SetValue(null, assembly);
        }
    }

#pragma warning disable SYSLIB1054 // One call in a test project does not justify compiling it with unsafe code.
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
#pragma warning restore SYSLIB1054
}
