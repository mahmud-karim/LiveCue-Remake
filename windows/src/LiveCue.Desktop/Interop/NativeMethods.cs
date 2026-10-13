using System.Runtime.InteropServices;

namespace LiveCue.Desktop.Interop;

/// <summary>The few Win32 calls the app makes. Signatures only; behaviour lives in the callers.</summary>
internal static partial class NativeMethods
{
    public const int DwmwaUseImmersiveDarkMode = 20;
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwcpRound = 2;

    public const uint MonitorDefaultToNearest = 2;

    public const int AttachParentProcess = -1;
    public const int StdOutputHandle = -11;

    public const int WmGetMinMaxInfo = 0x0024;

    /// <summary>WDA_NONE: the window appears in screen captures.</summary>
    public const uint WdaNone = 0x00;

    /// <summary>WDA_EXCLUDEFROMCAPTURE: captures show nothing where the window is (Windows 10 2004 and later).</summary>
    public const uint WdaExcludeFromCapture = 0x11;

    /// <summary>DPI_AWARENESS_CONTEXT_UNAWARE: windows created under it are laid out at 96 DPI.</summary>
    public static readonly IntPtr DpiAwarenessContextUnaware = new(-1);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(IntPtr hwnd, ref Point point);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetStdHandle(int handle);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }
}
