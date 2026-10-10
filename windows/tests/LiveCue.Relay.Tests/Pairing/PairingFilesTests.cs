using LiveCue.Relay.Pairing;
using LiveCue.Relay.Protocol;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>The production file system of the pairing store, on real files in a temporary folder.</summary>
public sealed class PairingFilesTests
{
    // W-design 4.7, "Atomic write".
    [Fact]
    public void Write_is_atomic_and_overwrites_a_leftover_pending_file()
    {
        using var folder = new TempFolder();
        var files = new PairingFiles();
        var path = folder.Combine("pairing.json");
        var pending = path + ".pending";

        // A first write: the file appears whole, and no pending file stays behind.
        Assert.False(files.Exists(path));
        files.WriteAtomic(path, "first"u8);
        Assert.True(files.Exists(path));
        Assert.Equal("first"u8.ToArray(), files.ReadAllBytes(path));
        Assert.Equal(["pairing.json"], folder.FileNames());

        // A pending file left by an interrupted write is overwritten, not appended to and not an error.
        File.WriteAllText(pending, "left behind by an interrupted write, and longer than what follows");
        files.WriteAtomic(path, "second"u8);
        Assert.Equal("second"u8.ToArray(), files.ReadAllBytes(path));
        Assert.Equal(["pairing.json"], folder.FileNames());

        // A write that cannot complete leaves the old content, never a mixture: here the pending file
        // cannot even be created, because something else holds it.
        using (new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => files.WriteAtomic(path, "third"u8));
        }

        Assert.Equal("second"u8.ToArray(), files.ReadAllBytes(path));

        // And here the pending file is written but cannot take the place of the pairing file.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var failure = Record.Exception(() => files.WriteAtomic(path, "fourth"u8));
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }

        Assert.Equal("second"u8.ToArray(), files.ReadAllBytes(path));
        Assert.Equal("fourth"u8.ToArray(), File.ReadAllBytes(pending));

        // The next write succeeds and clears the leftover again.
        files.WriteAtomic(path, "fifth"u8);
        Assert.Equal("fifth"u8.ToArray(), files.ReadAllBytes(path));
        Assert.Equal(["pairing.json"], folder.FileNames());
    }

    // W-05: a file larger than a pairing file can be is not read.
    [Fact]
    public void A_file_up_to_the_limit_is_read_whole_and_a_larger_one_is_refused()
    {
        using var folder = new TempFolder();
        var files = new PairingFiles();
        var path = folder.Combine("pairing.json");

        File.WriteAllBytes(path, new byte[ProtocolConstants.MaxStateFileBytes]);
        Assert.Equal(ProtocolConstants.MaxStateFileBytes, files.ReadAllBytes(path).Length);

        File.WriteAllBytes(path, new byte[ProtocolConstants.MaxStateFileBytes + 1]);
        Assert.ThrowsAny<IOException>(() => files.ReadAllBytes(path));

        Assert.ThrowsAny<IOException>(() => files.ReadAllBytes(folder.Combine("missing.json")));
    }
}
