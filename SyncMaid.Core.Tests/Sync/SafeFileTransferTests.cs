using System.Text;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.Sync;

/// <summary>
/// Proves the safety properties of <see cref="SafeFileTransfer"/> by injecting faults
/// (interrupted write, silent corruption, no space) into the in-memory filesystem and
/// asserting the destination is never left corrupted and a move never loses the source.
/// </summary>
public class SafeFileTransferTests
{
    private const string Source = @"S:\src\a.txt";
    private const string Dest = @"D:\dst\a.txt";

    private static InMemoryFileSystem WithSource(string contents = "hello world")
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(Source, contents);
        return fs;
    }

    private static string Read(InMemoryFileSystem fs, string path) =>
        Encoding.UTF8.GetString(fs.ReadAllBytes(path));

    private static bool HasTempFiles(InMemoryFileSystem fs) =>
        fs.AllPaths.Any(p => p.Contains(".syncmaid-tmp-"));

    [Fact]
    public void Copy_writes_the_destination_and_preserves_the_source_stamp()
    {
        var fs = WithSource("payload");

        SafeFileTransfer.Copy(fs, Source, Dest, verifyContents: true);

        Assert.Equal("payload", Read(fs, Dest));
        Assert.Equal(fs.GetStamp(Source), fs.GetStamp(Dest)); // no re-copy next run
        Assert.False(HasTempFiles(fs)); // temp cleaned up
    }

    [Fact]
    public void Interrupted_copy_leaves_the_existing_destination_untouched()
    {
        var fs = WithSource("new good data");
        fs.AddFile(Dest, "PREVIOUS GOOD COPY");
        fs.FailWrites = true; // transfer dies mid-write

        Assert.ThrowsAny<IOException>(() => SafeFileTransfer.Copy(fs, Source, Dest, verifyContents: false));

        Assert.Equal("PREVIOUS GOOD COPY", Read(fs, Dest)); // not overwritten with a partial file
        Assert.False(HasTempFiles(fs));
    }

    [Fact]
    public void Corrupt_copy_with_content_verification_is_not_committed()
    {
        var fs = WithSource("the real bytes");
        fs.AddFile(Dest, "PREVIOUS GOOD COPY");
        fs.CorruptWrites = true; // silent corruption, same length

        Assert.Throws<SyncVerificationException>(() => SafeFileTransfer.Copy(fs, Source, Dest, verifyContents: true));

        Assert.Equal("PREVIOUS GOOD COPY", Read(fs, Dest)); // corrupt copy rejected
        Assert.False(HasTempFiles(fs));
    }

    [Fact]
    public void Without_content_verification_same_length_corruption_is_not_caught()
    {
        // Documents the tier boundary: the basic length check cannot see a same-length
        // bit-flip — that is exactly what the opt-in content verification is for.
        var fs = WithSource("abcdef");
        fs.CorruptWrites = true;

        SafeFileTransfer.Copy(fs, Source, Dest, verifyContents: false);

        Assert.NotEqual("abcdef", Read(fs, Dest));
        Assert.Equal(6, fs.GetStamp(Dest).Length); // same length, only bytes differ
    }

    [Fact]
    public void Copy_fails_fast_when_there_is_not_enough_free_space()
    {
        var fs = WithSource("0123456789");
        fs.AddFile(Dest, "PREVIOUS GOOD COPY");
        fs.AvailableFreeSpace = 3; // less than the source length

        Assert.ThrowsAny<IOException>(() => SafeFileTransfer.Copy(fs, Source, Dest, verifyContents: false));

        Assert.Equal("PREVIOUS GOOD COPY", Read(fs, Dest));
        Assert.False(HasTempFiles(fs));
    }

    [Fact]
    public void Cleanup_failure_does_not_replace_the_original_verification_failure()
    {
        var fs = WithSource("the real bytes");
        fs.AddFile(Dest, "PREVIOUS GOOD COPY");
        fs.CorruptWrites = true;
        fs.FailDeletePathFragment = ".syncmaid-tmp-";

        var exception = Assert.Throws<SyncVerificationException>(() =>
            SafeFileTransfer.Copy(fs, Source, Dest, verifyContents: true));

        Assert.Contains("xxHash mismatch", exception.Message);
        Assert.Equal("PREVIOUS GOOD COPY", Read(fs, Dest));
    }

}
