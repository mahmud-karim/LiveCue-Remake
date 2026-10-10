using System.Security;
using LiveCue.Relay.Protocol;

namespace LiveCue.Hosting;

/// <summary>
/// Owns <c>settings.json</c> in the data folder (architecture 3.5, W-design 4.14).
///
/// A missing file means the defaults. A file that cannot be read or is not the expected JSON is
/// "unreadable"; a file that parses and breaks a rule is "invalid". Neither is ever changed by a load:
/// the application runs on the defaults, and of an invalid file it keeps every ledger entry that is
/// valid by itself, so that a mapping LiveCue created stays known and removable. The first save after
/// that copies the failed file to <c>settings.json.invalid</c> before it replaces it, so that nothing
/// the user wrote is lost.
/// </summary>
internal sealed class SettingsStore : ISettingsStore
{
    public const string FileName = "settings.json";

    /// <summary>The copy of a file that failed, kept beside it by the first save that replaces it.</summary>
    public const string InvalidCopyName = FileName + ".invalid";

    private const string SaveFailedMessage = "The settings could not be saved.";
    private const string CopyFailedMessage = "The settings file that failed could not be copied, so nothing was saved.";

    private readonly string _dataDirectory;
    private readonly IHostFileSystem _files;
    private readonly ISettingsValidator _validator;
    private readonly Lock _gate = new();

    public SettingsStore(string dataDirectory, IHostFileSystem files, ISettingsValidator validator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(validator);
        _dataDirectory = dataDirectory;
        _files = files;
        _validator = validator;
        FilePath = Path.Combine(dataDirectory, FileName);
        InvalidCopyPath = Path.Combine(dataDirectory, InvalidCopyName);
    }

    /// <summary>Where the file lives.</summary>
    public string FilePath { get; }

    /// <summary>Where the copy of a failed file is kept.</summary>
    public string InvalidCopyPath { get; }

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            return Read().Result;
        }
    }

    public void Save(RelaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = _validator.Validate(settings);
        if (problems.Count > 0)
        {
            throw new SettingsValidationException(problems);
        }

        var contents = SettingsJson.Write(settings);
        lock (_gate)
        {
            var (current, currentBytes) = Read();
            if (current.State is SettingsFileState.Invalid or SettingsFileState.Unreadable)
            {
                // What the user wrote is kept before it is replaced. Without that copy nothing is written.
                if (currentBytes is null)
                {
                    throw new IOException(CopyFailedMessage);
                }

                Write(InvalidCopyPath, currentBytes, CopyFailedMessage);
            }

            Write(FilePath, contents, SaveFailedMessage);
        }
    }

    private static bool IsFileSystemFailure(Exception exception) => exception
        is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException;

    /// <summary>What a load reports, and the bytes of the file when it could be read at all.</summary>
    private (SettingsLoadResult Result, byte[]? Bytes) Read()
    {
        byte[] bytes;
        try
        {
            if (!_files.FileExists(FilePath))
            {
                return (new SettingsLoadResult(RelaySettings.Default, SettingsFileState.Missing, []), null);
            }

            bytes = _files.ReadAllBytes(FilePath);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return (Unreadable, null);
        }

        // W-05: a larger file is not parsed at all.
        if (bytes.Length > ProtocolConstants.MaxStateFileBytes || !SettingsJson.TryRead(bytes, out var settings))
        {
            return (Unreadable, bytes);
        }

        // Only the rules that need no file system: a folder deleted later does not invalidate the file.
        var problems = _validator.ValidateStructure(settings);
        if (problems.Count == 0)
        {
            return (new SettingsLoadResult(settings, SettingsFileState.Loaded, []), bytes);
        }

        OwnedMapping[] kept = [.. settings.OwnedMappings.Where(_validator.IsValidLedgerEntry)];
        return (new SettingsLoadResult(RelaySettings.Default with { OwnedMappings = kept }, SettingsFileState.Invalid, problems), bytes);
    }

    private static SettingsLoadResult Unreadable => new(RelaySettings.Default, SettingsFileState.Unreadable, []);

    /// <summary>One atomic write. Every failure is an <see cref="IOException"/> with a fixed message.</summary>
    private void Write(string path, byte[] contents, string failureMessage)
    {
        try
        {
            if (!_files.DirectoryExists(_dataDirectory))
            {
                _files.CreateDirectory(_dataDirectory);
            }

            _files.WriteAllBytesAtomic(path, contents);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new IOException(failureMessage, exception);
        }
    }
}
