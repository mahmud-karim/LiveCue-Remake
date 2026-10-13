using System.Security.Cryptography;
using LiveCue.Relay.Core;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Answers;

/// <summary>
/// One run of <c>codex exec</c> (protocol 12.2; W-design 4.9). The request reaches Codex only as the
/// delimited, untrusted block of the prompt on standard input. The process gets the allowlisted
/// environment and the containment arguments of protocol 12.6, and a folder of its own:
/// <c>&lt;TempRoot&gt;\&lt;32 hex characters&gt;</c>, new for this run and empty when Codex starts.
/// Whatever the outcome, the folder and everything Codex wrote into it are gone when the run returns.
/// </summary>
internal sealed class CodexAnswerBackend : IAnswerBackend
{
    private const string Source = "answers";

    private readonly RelayContext _context;
    private readonly Func<string> _folderName;

    public CodexAnswerBackend(RelayContext context)
        : this(context, RandomFolderName)
    {
    }

    /// <summary>For tests: the folder name is given instead of drawn.</summary>
    internal CodexAnswerBackend(RelayContext context, Func<string> folderName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(folderName);
        _context = context;
        _folderName = folderName;
    }

    /// <summary>32 lower-case hex characters from the operating system's random source: not guessable.</summary>
    internal static string RandomFolderName() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public async Task<byte[]?> RunAsync(AnswerRun run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        ct.ThrowIfCancellationRequested();

        // Step 1.
        if (_context.Options.CodexExecutablePath is not { } codex)
        {
            return null;
        }

        // Step 2. An existing folder of that name is not this run's folder: the run fails and the folder
        // is left alone.
        if (TryCreateFolder() is not { } folder)
        {
            return null;
        }

        try
        {
            // Steps 3 to 5.
            var request = new ProcessRequest
            {
                FileName = codex,
                Arguments = CodexArguments.Build(
                    run.Model,
                    run.ReasoningEffort,
                    SchemaFiles.PathFor(_context.Options.DataDirectory, run.Kind),
                    folder),
                WorkingDirectory = folder,
                Environment = EnvironmentPolicy.CodexAllowlist,
                StandardInput = Prompts.Build(run.Kind, run.RequestBodyUtf8.Span),
                MaxStandardOutputBytes = 0,
                Timeout = ProtocolConstants.AnswerDeadline,
                KillWithApplication = true,
            };

            ProcessResult result;
            try
            {
                result = await _context.Processes.RunAsync(request, ct).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // The Codex path is not the absolute path of an .exe file: it cannot be started.
                return null;
            }

            // Step 6.
            if (result.Outcome == ProcessOutcome.Cancelled)
            {
                throw new OperationCanceledException(ct);
            }

            return result is { Outcome: ProcessOutcome.Exited, ExitCode: 0 }
                ? TryReadOutput(Path.Combine(folder, CodexArguments.OutputFileName))
                : null;
        }
        finally
        {
            // Step 7.
            await DeleteFolderAsync(folder).ConfigureAwait(false);
        }
    }

    /// <summary>The run's folder, newly created, or null when it cannot be new.</summary>
    private string? TryCreateFolder()
    {
        try
        {
            var root = _context.TempRoot;
            var folder = Path.Combine(root, _folderName());
            Directory.CreateDirectory(root);
            if (Directory.Exists(folder) || File.Exists(folder))
            {
                return null;
            }

            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception exception) when (IsFileProblem(exception))
        {
            return null;
        }
    }

    /// <summary>The bytes of the output file when it exists and is at most C-24 long; otherwise null.</summary>
    private static byte[]? TryReadOutput(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > ProtocolConstants.MaxCodexOutputBytes)
            {
                return null;
            }

            // One byte more than the limit is asked for, so that a file that grew is still refused.
            var buffer = new byte[ProtocolConstants.MaxCodexOutputBytes + 1];
            var count = file.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return count > ProtocolConstants.MaxCodexOutputBytes ? null : buffer.AsSpan(0, count).ToArray();
        }
        catch (Exception exception) when (IsFileProblem(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the folder with everything in it: W-09 tries, on the relay's clock. A folder that is
    /// still there afterwards is reported, and the next relay start removes it (W-design 4.2 step 5).
    /// </summary>
    private async Task DeleteFolderAsync(string folder)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                DeleteTree(folder);
                return;
            }
            catch (Exception exception) when (IsFileProblem(exception))
            {
                if (attempt >= ProtocolConstants.FolderDeleteAttempts)
                {
                    _context.Events.Diagnostic(Source, "A working folder of an answer run could not be deleted.", exception);
                    return;
                }
            }

            await Task.Delay(ProtocolConstants.FolderDeleteGap, _context.Clock).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One try. A file that was made read-only cannot be deleted as it is, so on a refusal the attribute
    /// is taken off everything in the folder and the deletion is tried once more. A link inside the
    /// folder is removed as a link: nothing outside the folder is ever touched.
    /// </summary>
    private static void DeleteTree(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            var inside = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            };
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", inside))
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }

            Directory.Delete(folder, recursive: true);
        }
    }

    private static bool IsFileProblem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
