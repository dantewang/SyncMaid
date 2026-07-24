using SyncMaid.Core.IO;

namespace SyncMaid.Core.Tests.IO;

/// <summary>
/// A disk-free <see cref="IFileSystem"/> for tests. Files live in a dictionary keyed
/// by a normalized absolute path; each holds its bytes and a <see cref="FileStamp"/>.
/// Directories are implied by file paths, plus roots registered via
/// <see cref="EnsureDirectory"/> — so, matching <see cref="PhysicalFileSystem"/>,
/// enumeration can distinguish a created-but-empty root from a missing one.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private sealed record Entry(byte[] Contents, FileStamp Stamp);

    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    // Registered directories with their modified times; directories implied by file
    // paths (or ancestor chains) report DefaultTime until a time is set explicitly.
    private readonly Dictionary<string, DateTime> _directories = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>When equal to a path, <see cref="WriteAllBytes"/> stores the bytes and then throws.</summary>
    public string? FailWriteAllBytesAfterMutationPath { get; set; }

    /// <summary>When equal to a path, <see cref="ReadAllBytes"/> throws for that path.</summary>
    public string? FailReadAllBytesPath { get; set; }

    private readonly HashSet<string> _lockedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marks a path as held open by another process: <see cref="OpenRead"/> then
    /// reports a Win32 sharing violation for it, the shape <see cref="FileBusy"/> recognizes.</summary>
    public void LockPath(string path) => _lockedPaths.Add(Normalize(path));

    /// <summary>When contained in a path, <see cref="CreateWriteThrough"/> throws for that
    /// path — a destination-side failure that is <b>not</b> a lock (so it is a real failure,
    /// not a deferral).</summary>
    public string? FailWritePathFragment { get; set; }

    /// <summary>The exception Windows raises when a file is held open by another process.</summary>
    public static IOException SharingViolation(string path) =>
        new(
            $"The process cannot access the file '{path}' because it is being used by another process.",
            unchecked((int)0x80070020));

    /// <summary>When equal to a path, <see cref="DeleteFile"/> throws for that path.</summary>
    public string? FailDeletePath { get; set; }

    /// <summary>Creates the exception used for an injected <see cref="DeleteFile"/> failure.</summary>
    public Func<Exception> DeleteFailure { get; set; } =
        () => new IOException("Simulated delete failure.");

    /// <summary>When contained in a path, <see cref="DeleteFile"/> throws for that path.</summary>
    public string? FailDeletePathFragment { get; set; }

    /// <summary>When equal to a path, <see cref="DeleteFile"/> removes it and then throws.</summary>
    public string? FailDeleteAfterMutationPath { get; set; }

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

    /// <summary>The <see cref="IFileSystem"/> walk: files with stamps plus directories with
    /// times, in one call. The path-only enumerations below stay as test conveniences.</summary>
    public TreeListing ListTree(string root)
    {
        var prefix = RequireRoot(root);
        var files = EnumerateCore(prefix)
            .Select(relative => new ListedFile(relative, _files[prefix + relative].Stamp))
            .ToList();
        var directories = EnumerateDirectoriesCore(prefix)
            .Select(relative => new ListedDirectory(
                relative, FileStamp.NormalizeUtc(_directories.GetValueOrDefault(prefix + relative, DefaultTime))))
            .ToList();
        return new TreeListing(files, directories);
    }

    public IEnumerable<string> EnumerateFiles(string root) => EnumerateCore(RequireRoot(root));

    // Matches PhysicalFileSystem: a missing root is not an empty one. A root exists when
    // it was registered, anything registered lives beneath it, or a file implies it.
    private string RequireRoot(string root)
    {
        var normalizedRoot = Normalize(root);
        var prefix = normalizedRoot + "/";
        var exists = _directories.ContainsKey(normalizedRoot)
            || _directories.Keys.Any(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || _files.Keys.Any(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            throw new DirectoryNotFoundException($"Folder not found or unavailable: {root}");
        }

        return prefix;
    }

    private IEnumerable<string> EnumerateCore(string prefix)
    {
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

    public IEnumerable<string> EnumerateDirectories(string root) =>
        EnumerateDirectoriesCore(RequireRoot(root));

    // Directories are implied by file paths (every ancestor of a file exists) plus
    // whatever was registered explicitly via EnsureDirectory — including the
    // registered directory's own ancestors, as on a real filesystem.
    private IEnumerable<string> EnumerateDirectoriesCore(string prefix)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _files.Keys)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                AddDirectoryChain(directories, path[prefix.Length..], includeSelf: false);
            }
        }

        foreach (var directory in _directories.Keys)
        {
            if (directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                AddDirectoryChain(directories, directory[prefix.Length..], includeSelf: true);
            }
        }

        return directories;
    }

    private static void AddDirectoryChain(HashSet<string> directories, string relative, bool includeSelf)
    {
        for (var i = relative.IndexOf('/'); i >= 0; i = relative.IndexOf('/', i + 1))
        {
            directories.Add(relative[..i]);
        }

        if (includeSelf)
        {
            directories.Add(relative);
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
        if (string.Equals(
                Normalize(path),
                Normalize(FailWriteAllBytesAfterMutationPath ?? ""),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Simulated write failure after mutation.");
        }
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
        if (string.Equals(
                Normalize(path),
                Normalize(FailDeleteAfterMutationPath ?? ""),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Simulated delete failure after mutation.");
        }
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
        // Tracked so EnumerateFiles can tell a created-but-empty root from a missing
        // one. Tests seed an existing empty folder with this too. TryAdd: re-ensuring
        // an existing directory must not reset a time a test (or the applier) set.
        _directories.TryAdd(Normalize(path), DefaultTime);
    }

    /// <summary>Sets a directory's modified time; registers an implied directory as a
    /// side effect, as the real filesystem's set-time succeeds on any existing folder.</summary>
    public void SetDirectoryLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        _directories[Normalize(path)] = lastWriteTimeUtc;
    }

    /// <summary>Paths removed by <see cref="DeleteEmptyDirectory"/>, for assertions.
    /// Directories here are implied by file paths, so a directory whose files were all
    /// deleted counts as empty even if it was never registered explicitly.</summary>
    public List<string> DeletedDirectories { get; } = [];

    public void DeleteEmptyDirectory(string path)
    {
        var key = Normalize(path);
        var prefix = key + "/";
        if (_files.Keys.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            // Not empty — matches PhysicalFileSystem's non-recursive, best-effort skip.
            return;
        }

        _directories.Remove(key);
        DeletedDirectories.Add(key);
    }

    public Stream OpenRead(string path)
    {
        if (_lockedPaths.Contains(Normalize(path)))
        {
            throw SharingViolation(path);
        }

        if (_files.TryGetValue(Normalize(path), out var entry))
        {
            return new MemoryStream(entry.Contents, writable: false);
        }

        throw new FileNotFoundException("No such file in the in-memory filesystem.", path);
    }

    public Stream CreateWriteThrough(string path)
    {
        if (FailWritePathFragment is not null
            && path.Contains(FailWritePathFragment, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Simulated destination write failure.");
        }

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
