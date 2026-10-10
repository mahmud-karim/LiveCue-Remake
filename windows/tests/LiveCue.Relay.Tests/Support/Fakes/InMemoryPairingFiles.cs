using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Tests.Support;

/// <summary>The pairing store's files in memory, with failure injection. Paths are compared without regard to case.</summary>
internal sealed class InMemoryPairingFiles : IPairingFiles
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True: every write throws, and the file keeps its old content.</summary>
    public bool WritesFail { get; set; }

    /// <summary>True: every read throws.</summary>
    public bool ReadsFail { get; set; }

    public int Writes { get; private set; }

    public int Reads { get; private set; }

    /// <summary>The paths that exist, sorted.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate)
            {
                return [.. _files.Keys.Order(StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>Puts a file there, as if an earlier run had written it.</summary>
    public void Put(string path, byte[] contents)
    {
        lock (_gate)
        {
            _files[path] = [.. contents];
        }
    }

    /// <summary>The content of a file, or null when it does not exist.</summary>
    public byte[]? Get(string path)
    {
        lock (_gate)
        {
            return _files.TryGetValue(path, out var contents) ? [.. contents] : null;
        }
    }

    public bool Exists(string path)
    {
        lock (_gate)
        {
            return _files.ContainsKey(path);
        }
    }

    public byte[] ReadAllBytes(string path)
    {
        lock (_gate)
        {
            Reads++;
            if (ReadsFail)
            {
                throw new IOException("The file could not be read.");
            }

            return _files.TryGetValue(path, out var contents)
                ? [.. contents]
                : throw new FileNotFoundException("The file does not exist.");
        }
    }

    public void WriteAtomic(string path, ReadOnlySpan<byte> contents)
    {
        lock (_gate)
        {
            if (WritesFail)
            {
                throw new IOException("The file could not be written.");
            }

            Writes++;
            _files[path] = contents.ToArray();
        }
    }
}
