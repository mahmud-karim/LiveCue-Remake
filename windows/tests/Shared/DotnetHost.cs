namespace LiveCue.TestSupport;

/// <summary>
/// The <c>dotnet</c> host that runs the tests, for tests that start a real child program
/// (<c>dotnet LiveCue.TestChild.dll …</c>, <c>dotnet LiveCueRemake.dll --self-test</c>).
///
/// Looked for in this order: the <c>DOTNET_HOST_PATH</c> variable that <c>dotnet test</c> sets for its
/// children; the <c>LIVECUE_DOTNET_EXE</c> variable (the full path of dotnet.exe on a PC without an
/// installed SDK); the search path.
/// </summary>
internal static class DotnetHost
{
    private static readonly Lazy<string> Resolved = new(Resolve);

    /// <summary>The absolute path of dotnet.exe.</summary>
    /// <exception cref="InvalidOperationException">No host was found.</exception>
    public static string Path => Resolved.Value;

    /// <summary>The absolute path of a file that the build put beside the test assembly.</summary>
    public static string Beside(string fileName) => System.IO.Path.Combine(AppContext.BaseDirectory, fileName);

    private static string Resolve()
    {
        foreach (var variable in (string[])["DOTNET_HOST_PATH", "LIVECUE_DOTNET_EXE"])
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
            {
                return System.IO.Path.GetFullPath(value);
            }
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in searchPath.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = System.IO.Path.Combine(folder, "dotnet.exe");
                if (File.Exists(candidate))
                {
                    return System.IO.Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
                // A malformed entry of the search path is skipped.
            }
        }

        throw new InvalidOperationException(
            "The dotnet host was not found. Set LIVECUE_DOTNET_EXE to the full path of dotnet.exe, or put dotnet on the search path.");
    }
}
