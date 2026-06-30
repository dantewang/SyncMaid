using System.Buffers;
using System.IO.Hashing;
using SyncMaid.Core.IO;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Copies and moves a single file <b>safely</b>: the destination is never left
/// truncated or corrupted, even if the process is killed, the disk fills, or the
/// source read fails mid-stream. The algorithm is "temp → verify → atomic rename":
/// the existing destination is only ever replaced by a complete, verified file.
/// </summary>
/// <remarks>
/// <para>
/// The orchestration lives here (in UI-free Core), not inside <see cref="IFileSystem"/>,
/// so it is fully testable with an in-memory filesystem that can inject faults
/// (interrupted write, silent corruption) and prove the safety properties hold.
/// </para>
/// <para>
/// Verification has two tiers (see the location-and-verification design doc):
/// <list type="bullet">
///   <item><b>Basic</b> (always): a length check after the write — catches truncation
///   / partial writes, the dominant corruption mode — plus the atomic rename, so a
///   partial file is never visible at the destination.</item>
///   <item><b>Content</b> (opt-in, <paramref name="verifyContents"/>): the written
///   temp is read back and its xxHash compared to the source, catching silent
///   hardware / environmental corruption that a length check cannot.</item>
/// </list>
/// </para>
/// </remarks>
public static class SafeFileTransfer
{
    private const int BufferSize = 1 << 16; // 64 KiB streaming buffer.
    private const string TempSuffix = ".syncmaid-tmp-";

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/>, leaving the
    /// source in place. The destination is replaced atomically and only after the copy
    /// passes verification.
    /// </summary>
    public static void Copy(IFileSystem fileSystem, string source, string destination, bool verifyContents)
    {
        var sourceStamp = fileSystem.GetStamp(source);

        // Preflight: fail fast rather than fill the volume and leave a partial temp.
        if (fileSystem.GetAvailableFreeSpace(destination) < sourceStamp.Length)
        {
            throw new IOException(
                $"Not enough free space at the destination for '{source}' ({sourceStamp.Length} bytes).");
        }

        var temp = destination + TempSuffix + Guid.NewGuid().ToString("N");
        try
        {
            UInt128 sourceHash;
            using (var input = fileSystem.OpenRead(source))
            using (var output = fileSystem.CreateWriteThrough(temp))
            {
                sourceHash = CopyAndHash(input, output);
                output.Flush();
            }

            // Preserve the source timestamp so source and copy share a FileStamp and are
            // not seen as "changed" on the next run.
            fileSystem.SetLastWriteTimeUtc(temp, sourceStamp.LastWriteTimeUtc);

            // Basic verification: length must match.
            var tempStamp = fileSystem.GetStamp(temp);
            if (tempStamp.Length != sourceStamp.Length)
            {
                throw new SyncVerificationException(
                    $"Copy of '{source}' has the wrong length ({tempStamp.Length} vs {sourceStamp.Length}).");
            }

            // Content verification (opt-in): read the persisted bytes back and compare.
            if (verifyContents)
            {
                UInt128 writtenHash;
                using (var readBack = fileSystem.OpenRead(temp))
                {
                    writtenHash = Hash(readBack);
                }

                if (writtenHash != sourceHash)
                {
                    throw new SyncVerificationException(
                        $"Copy of '{source}' failed content verification (xxHash mismatch).");
                }
            }

            // Commit: atomic rename over the destination.
            fileSystem.Replace(temp, destination);
        }
        catch
        {
            // Leave the existing destination untouched; clean up our temp.
            fileSystem.DeleteFile(temp);
            throw;
        }
    }

    /// <summary>
    /// Moves <paramref name="source"/> to <paramref name="destination"/>: an atomic,
    /// verified copy, then the source is deleted <b>only</b> after the destination is
    /// confirmed to match. A failed copy never deletes the source.
    /// </summary>
    public static void Move(IFileSystem fileSystem, string source, string destination, bool verifyContents)
    {
        Copy(fileSystem, source, destination, verifyContents);

        // Defense in depth: do not remove the source unless the destination is provably
        // the same file. Copy already verified, but this guards against a racing change.
        if (fileSystem.GetStamp(destination) != fileSystem.GetStamp(source))
        {
            throw new SyncVerificationException(
                $"Refusing to delete source '{source}': destination does not match after copy.");
        }

        fileSystem.DeleteFile(source);
    }

    // Streams input → output, computing the xxHash of the bytes as they pass through
    // (hash-on-the-fly: one read of the source, no extra pass).
    private static UInt128 CopyAndHash(Stream input, Stream output)
    {
        var hasher = new XxHash128();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, read));
                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hasher.GetCurrentHashAsUInt128();
    }

    // Hashes a stream's full contents (used for the read-back compare).
    private static UInt128 Hash(Stream stream)
    {
        var hasher = new XxHash128();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hasher.GetCurrentHashAsUInt128();
    }
}
