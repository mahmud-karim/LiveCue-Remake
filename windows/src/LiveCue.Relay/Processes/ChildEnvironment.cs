using System.Collections;

namespace LiveCue.Relay.Processes;

/// <summary>
/// The three environment policies of <see cref="EnvironmentPolicy"/> (W-design 4.13). A child's
/// environment is cleared and then refilled from the parent's: names are compared without regard to
/// case, and a value is copied only when the parent has that variable. Nothing is ever invented.
///
/// The two lists below are the only place where the allowed names are written.
/// </summary>
internal static class ChildEnvironment
{
    /// <summary>The thirteen names of protocol 11.5: what a program needs to run on Windows.</summary>
    private static readonly string[] LabScriptNames =
    [
        "SystemRoot",
        "windir",
        "Path",
        "PathExt",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "APPDATA",
        "LOCALAPPDATA",
        "ProgramData",
        "ProgramFiles",
        "ProgramFiles(x86)",
        "ComSpec",
    ];

    /// <summary>
    /// Protocol 12.6: the thirteen, what Codex needs to find its own sign-in, and the proxy settings of
    /// the PC. No key or token the owner keeps in the environment is on this list.
    /// </summary>
    private static readonly string[] CodexNames =
    [
        .. LabScriptNames,
        "HOMEDRIVE",
        "HOMEPATH",
        "CODEX_HOME",
        "HTTPS_PROXY",
        "HTTP_PROXY",
        "ALL_PROXY",
        "NO_PROXY",
    ];

    /// <summary>
    /// Replaces everything in <paramref name="target"/> (the environment of a process that is about to
    /// start) with what <paramref name="policy"/> lets through of this process's own environment.
    /// </summary>
    public static void Fill(IDictionary<string, string?> target, EnvironmentPolicy policy) =>
        Fill(target, policy, Environment.GetEnvironmentVariables());

    /// <summary>The same, from a given parent environment.</summary>
    public static void Fill(IDictionary<string, string?> target, EnvironmentPolicy policy, IDictionary parent)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parent);

        var allowed = policy switch
        {
            EnvironmentPolicy.Inherit => null,
            EnvironmentPolicy.CodexAllowlist => CodexNames,
            EnvironmentPolicy.LabScriptAllowlist => LabScriptNames,
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

        target.Clear();
        foreach (DictionaryEntry entry in parent)
        {
            if (entry.Key is not string name || entry.Value is not string value)
            {
                continue;
            }

            if (allowed is null || IsAllowed(allowed, name))
            {
                // The parent's own spelling of the name is kept.
                target[name] = value;
            }
        }
    }

    private static bool IsAllowed(string[] allowed, string name)
    {
        foreach (var candidate in allowed)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
