namespace SyncMaid.Core.IO;

/// <summary>
/// A cheap, deterministic fingerprint of a file used to decide whether a source
/// and destination copy differ. We compare size and last-write-time rather than
/// hashing contents: it is fast, allocation-free, and good enough for a file-sync
/// tool. Two files are treated as equal when both fields match.
/// </summary>
/// <param name="Length">File length in bytes.</param>
/// <param name="LastWriteTimeUtc">Last write time, in UTC, truncated to whole seconds.</param>
public readonly record struct FileStamp(long Length, DateTime LastWriteTimeUtc)
{
    /// <summary>
    /// Builds a stamp, normalizing the timestamp to UTC whole seconds so that
    /// filesystems with differing sub-second precision still compare equal.
    /// </summary>
    public static FileStamp Create(long length, DateTime lastWriteTimeUtc) =>
        new(length, NormalizeUtc(lastWriteTimeUtc));

    /// <summary>
    /// The stamp normalization on its own — UTC, truncated to whole seconds — for
    /// timestamps compared outside a full stamp (directory modified times).
    /// </summary>
    public static DateTime NormalizeUtc(DateTime lastWriteTimeUtc)
    {
        var utc = lastWriteTimeUtc.ToUniversalTime();
        return new DateTime(
            utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
    }
}
