using System.Globalization;

namespace LiveCue.Relay.LocalModel;

/// <summary>
/// The relay's own launcher scripts (protocol 11.5). Three files are embedded in the assembly, linked from
/// <c>asr/</c> so that there is one source: the two launchers and <c>LabCommon.ps1</c>, which both launchers
/// load from their own folder. The relay writes them into <c>&lt;data folder&gt;\lab\</c> at each start,
/// checks them against the embedded bytes again before each run, and never runs a file of the lab folder.
/// This file also holds the two command lines.
/// </summary>
internal static class LabScripts
{
    public const string FolderName = "lab";
    public const string StartFileName = "Start-ASR-Lab.ps1";
    public const string StopFileName = "Stop-ASR-Lab.ps1";
    public const string CommonFileName = "LabCommon.ps1";

    private const string ResourcePrefix = "LiveCue.Relay.LocalModel.Scripts.";

    private static readonly Lazy<byte[]> Start = new(() => ReadResource(StartFileName));
    private static readonly Lazy<byte[]> Stop = new(() => ReadResource(StopFileName));
    private static readonly Lazy<byte[]> Common = new(() => ReadResource(CommonFileName));

    /// <summary>The three file names, launchers first.</summary>
    public static IReadOnlyList<string> FileNames { get; } = [StartFileName, StopFileName, CommonFileName];

    /// <summary>
    /// <c>WindowsPowerShell\v1.0\powershell.exe</c> under the system directory that the operating-system
    /// API reports. Never built from <c>%SystemRoot%</c> or any other environment variable.
    /// </summary>
    public static string PowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>The folder of the relay's copies.</summary>
    public static string FolderFor(string dataDirectory) => Path.Combine(dataDirectory, FolderName);

    /// <summary>The absolute path of one of the relay's copies.</summary>
    public static string PathFor(string dataDirectory, string fileName) =>
        Path.Combine(dataDirectory, FolderName, fileName);

    /// <summary>The embedded bytes of one of the three files. The caller must not change the array.</summary>
    public static ReadOnlyMemory<byte> Embedded(string fileName) => fileName switch
    {
        StartFileName => Start.Value,
        StopFileName => Stop.Value,
        CommonFileName => Common.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(fileName), "Not one of the relay's launcher scripts."),
    };

    /// <summary>
    /// Relay start (W-design 4.2 step 5): writes all three files from the embedded copies, replacing
    /// whatever is there. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when the folder or a file cannot be written.
    /// </summary>
    public static void WriteCopies(string dataDirectory)
    {
        Directory.CreateDirectory(FolderFor(dataDirectory));
        foreach (var fileName in FileNames)
        {
            File.WriteAllBytes(PathFor(dataDirectory, fileName), Embedded(fileName).Span);
        }
    }

    /// <summary>
    /// Immediately before a run: compares the launcher that is about to start and <c>LabCommon.ps1</c> with
    /// the embedded bytes and rewrites a copy that is missing or differs. False when the two files cannot
    /// be made equal to the embedded bytes; then nothing may be started.
    /// </summary>
    public static bool TryMakeCurrent(string dataDirectory, string launcherFileName)
    {
        try
        {
            Directory.CreateDirectory(FolderFor(dataDirectory));
            return TryMakeCurrentFile(dataDirectory, launcherFileName)
                && TryMakeCurrentFile(dataDirectory, CommonFileName);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return false;
        }
    }

    /// <summary>The start command of protocol 11.5, one list element per argument.</summary>
    public static IReadOnlyList<string> StartArguments(string dataDirectory, string labFolder, string model, int labPort) =>
    [
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        PathFor(dataDirectory, StartFileName),
        "-LabFolder",
        labFolder,
        "-Model",
        model,
        "-Port",
        labPort.ToString(CultureInfo.InvariantCulture),
        "-NoBrowser",
        "-EnsureDocker",
    ];

    /// <summary>The stop command of protocol 11.5, one list element per argument.</summary>
    public static IReadOnlyList<string> StopArguments(string dataDirectory, int labPort) =>
    [
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        PathFor(dataDirectory, StopFileName),
        "-Port",
        labPort.ToString(CultureInfo.InvariantCulture),
    ];

    private static bool TryMakeCurrentFile(string dataDirectory, string fileName)
    {
        var path = PathFor(dataDirectory, fileName);
        var embedded = Embedded(fileName);
        if (IsCurrent(path, embedded.Span))
        {
            return true;
        }

        File.WriteAllBytes(path, embedded.Span);

        // Read back: what PowerShell is about to load must be these bytes, not merely a write that did not throw.
        return IsCurrent(path, embedded.Span);
    }

    private static bool IsCurrent(string path, ReadOnlySpan<byte> embedded)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        // Never more than the embedded length is read, whatever is at that path.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != embedded.Length)
        {
            return false;
        }

        var bytes = new byte[embedded.Length];
        return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length
            && stream.ReadByte() < 0
            && bytes.AsSpan().SequenceEqual(embedded);
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException;

    private static byte[] ReadResource(string fileName)
    {
        using var stream = typeof(LabScripts).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException("An embedded launcher script is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
