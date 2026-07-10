using SyncMaid.Core.IO;

namespace SyncMaid.Core.Tests.IO;

/// <summary>
/// A disk-free <see cref="IFileSystem"/> for tests. Files live in a dictionary keyed
/// by a normalized absolute path; each holds its bytes and a <see cref="FileStamp"/>.
/// There is no real directory tree — directories are implied by file paths — so
/// <see cref="EnsureDirectory"/> is a no-op and enumeration is by path prefix.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private sealed record Entry(byte[] Contents, FileStamp Stamp);

    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly DateTime DefaultTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Free space reported by <see cref="GetAvailableFreeSpace"/>; tune for preflight tests.</summary>
    public long AvailableFreeSpace { get; set; } = long.MaxValue;

    /// <summary>When set, <see cref="CreateWriteThrough"/> returns a stream that throws on write
    /// — simulating an interrupted transfer (power loss, source read error).</summary>
    public bool FailWrites { get; set; }

    /// <summary>When &gt; 0, the next this-many writes throw (then succeed) — simulating a
    /// transiently locked file that clears after a retry. Decrements per failed write.</summary>
    public int FailWritesTimes { get; set; }

    /// <summary>When set, written bytes are silently flipped before being stored (same length)
    /// — simulating hardware/environmental corruption that only content verification can catch.</summary>
    public bool CorruptWrites { get; set; }

    /// <summary>When equal to a path, <see cref="WriteAllBytes"/> throws for that path.</summary>
    public string? FailWriteAllBytesPath { get; set; }

    /// <summary>When equal to a path, <see cref="ReadAllBytes"/> throws for that path.</summary>
    public string? FailReadAllBytesPath { get; set; }

    /// <summary>When equal to a path, <see cref="DeleteFile"/> throws for that path.</summary>
    public string? FailDeletePath { get; set; }

    /// <summary>Creates the exception used for an injected <see cref="DeleteFile"/> failure.</summary>
    public Func<Exception> DeleteFailure { get; set; } =
        () => new IOException("Simulated delete failure.");

    /// <summary>When contained in a path, <see cref="DeleteFile"/> throws for that path.</summary>
    public string? FailDeletePathFragment { get; set; }

    /// <summary>Number of upcoming enumerations that throw after <see cref="FailEnumerationAfter"/> items.</summary>
    public int EnumerationFailuresRemaining { get; set; }

    /// <summary>Number of items yielded before an injected enumeration failure.</summary>
    public int FailEnumerationAfter { get; set; }

    /// <summary>The exception an injected enumeration failure throws; IOException by default.</summary>
    public Func<Exception> EnumerationFailure { get; set; } =
        () => new IOException("Simulated mid-enumeration failure.");

    /// <summary>Offset applied by <see cref="SetLastWriteTimeUtc"/> to inject stamp mismatches.</summary>
    public TimeSpan SetLastWriteTimeOffset { get; set; }

    public int GetStampCallCount { get; private set; }
    private readonly Dictionary<string, int> _getStampCallsByPath = new(StringComparer.OrdinalIgnoreCase);

    public int GetStampCallCountFor(string path) =>
        _getStampCallsByPath.GetValueOrDefault(Normalize(path));

    /// <summary>Seeds a file with explicit contents and stamp; used to set up test state.</summary>
    public void AddFile(string path, byte[] contents, FileStamp stamp)
    {
        _files[Normalize(path)] = new Entry(contents, stamp);
    }

    /// <summary>Convenience overload: stamp derived from length and a fixed default time.</summary>
    public void AddFile(string path, string contents, DateTime? lastWriteUtc = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        var stamp = FileStamp.Create(bytes.Length, lastWriteUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AddFile(path, bytes, stamp);
    }

    /// <summary>All file paths currently present (normalized), for assertions.</summary>
    public IReadOnlyCollection<string> AllPaths => _files.Keys.ToList();

    public IEnumerable<string> EnumerateFiles(string root)
    {
        var prefix = Normalize(root).TrimEnd('/') + "/";
        var yielded = 0;
        foreach (var path in _files.Keys)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (EnumerationFailuresRemaining > 0 && yielded == FailEnumerationAfter)
                {
                    EnumerationFailuresRemaining--;
                    throw EnumerationFailure();
                }

                yielded++;
                yield return path[prefix.Length..];
            }
        }
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public FileStamp GetStamp(string path)
    {
        GetStampCallCount++;
        var normalizedPath = Normalize(path);
        _getStampCallsByPath[normalizedPath] = GetStampCallCountFor(normalizedPath) + 1;

        if (_files.TryGetValue(normalizedPath, out var entry))
        {
            return entry.Stamp;
        }

        throw new FileNotFoundException("No such file in the in-memory filesystem.", path);
    }

    public byte[] ReadAllBytes(string path)
    {
        if (string.Equals(Normalize(path), Normalize(FailReadAllBytesPath ?? ""), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Simulated read failure.");
        }

        if (_files.TryGetValue(Normalize(path), out var entry))
        {
            return entry.Contents;
        }

        throw new FileNotFoundException("No such file in the in-memory filesystem.", path);
    }

    public void WriteAllBytes(string path, byte[] contents)
    {
        if (string.Equals(Normalize(path), Normalize(FailWriteAllBytesPath ?? ""), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Simulated write failure.");
        }

        var stamp = FileStamp.Create(contents.Length, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _files[Normalize(path)] = new Entry(contents, stamp);
    }

    /// <summary>Paths sent to the Recycle Bin (rather than permanently deleted), for assertions.</summary>
    public List<string> Recycled { get; } = [];

    public void DeleteFile(string path)
    {
        if (string.Equals(Normalize(path), Normalize(FailDeletePath ?? ""), StringComparison.OrdinalIgnoreCase)
            || (FailDeletePathFragment is not null
                && path.Contains(FailDeletePathFragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw DeleteFailure();
        }

        _files.Remove(Normalize(path));
    }

    public void Recycle(string path)
    {
        var key = Normalize(path);
        if (_files.Remove(key))
        {
            Recycled.Add(key);
        }
    }

    public void EnsureDirectory(string path)
    {
        // No real directory tree; directories are implied by file paths.
    }

    public Stream OpenRead(string path)
    {
        if (_files.TryGetValue(Normalize(path), out var entry))
        {
            return new MemoryStream(entry.Contents, writable: false);
        }

        throw new FileNotFoundException("No such file in the in-memory filesystem.", path);
    }

    public Stream CreateWriteThrough(string path)
    {
        if (FailWrites)
        {
            return new FailingStream();
        }

        if (FailWritesTimes > 0)
        {
            FailWritesTimes--;
            return new FailingStream();
        }

        return new CapturingStream(this, Normalize(path), CorruptWrites);
    }

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        var key = Normalize(path);
        if (_files.TryGetValue(key, out var entry))
        {
            _files[key] = entry with
            {
                Stamp = FileStamp.Create(entry.Contents.Length, lastWriteTimeUtc + SetLastWriteTimeOffset),
            };
        }
    }

    public void Replace(string sourcePath, string destinationPath)
    {
        var sourceKey = Normalize(sourcePath);
        if (!_files.TryGetValue(sourceKey, out var source))
        {
            throw new FileNotFoundException("No such file in the in-memory filesystem.", sourcePath);
        }

        _files[Normalize(destinationPath)] = source;
        _files.Remove(sourceKey);
    }

    public long GetAvailableFreeSpace(string path) => AvailableFreeSpace;

    // Stores its bytes into the filesystem on dispose, mirroring a real file handle being
    // closed. Optionally flips a byte to model silent corruption (same length).
    private sealed class CapturingStream : MemoryStream
    {
        private readonly InMemoryFileSystem _owner;
        private readonly string _key;
        private readonly bool _corrupt;
        private bool _stored;

        public CapturingStream(InMemoryFileSystem owner, string key, bool corrupt)
        {
            _owner = owner;
            _key = key;
            _corrupt = corrupt;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_stored)
            {
                _stored = true;
                var bytes = ToArray();
                if (_corrupt && bytes.Length > 0)
                {
                    bytes[0] ^= 0xFF;
                }

                _owner._files[_key] = new Entry(bytes, FileStamp.Create(bytes.Length, DefaultTime));
            }

            base.Dispose(disposing);
        }
    }

    // A write stream that throws, modelling a transfer interrupted mid-copy. Nothing is
    // ever stored, so the destination is left untouched.
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) { }

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated interrupted write.");
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}
