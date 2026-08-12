using System;
using System.Collections.Generic;
using System.IO;
using SyncMaid.Core.IO;

namespace SyncMaid.UiTests.Fakes;

/// <summary>
/// A source tree for the destination workspace's preview, which only ever reads: it lists
/// the source and assigns the files. Every other operation throws, so a preview that starts
/// touching files fails loudly instead of quietly working.
/// </summary>
public sealed class FakeSourceFileSystem : IFileSystem
{
    private readonly Dictionary<string, List<string>> _trees = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a source root holding <paramref name="relativePaths"/>. A root that
    /// was never added is unavailable — the case an unplugged drive produces.</summary>
    public FakeSourceFileSystem With(string root, params string[] relativePaths)
    {
        _trees[root] = [.. relativePaths];
        return this;
    }

    public TreeListing ListTree(string root)
    {
        if (!_trees.TryGetValue(root, out var files))
        {
            throw new DirectoryNotFoundException($"Folder not found or unavailable: {root}");
        }

        return new TreeListing(
            files.ConvertAll(path => new ListedFile(path, FileStamp.Create(1, DateTime.UnixEpoch))),
            []);
    }

    public bool FileExists(string path) => throw new NotSupportedException();
    public FileStamp GetStamp(string path) => throw new NotSupportedException();
    public byte[] ReadAllBytes(string path) => throw new NotSupportedException();
    public void WriteAllBytes(string path, byte[] contents) => throw new NotSupportedException();
    public void DeleteFile(string path) => throw new NotSupportedException();
    public void Recycle(string path) => throw new NotSupportedException();
    public void EnsureDirectory(string path) => throw new NotSupportedException();
    public void DeleteEmptyDirectory(string path) => throw new NotSupportedException();
    public void RecycleEmptyDirectory(string path) => throw new NotSupportedException();
    public void SetDirectoryLastWriteTimeUtc(string path, DateTime utc) => throw new NotSupportedException();
    public Stream OpenRead(string path) => throw new NotSupportedException();
    public Stream CreateWriteThrough(string path) => throw new NotSupportedException();
    public void SetLastWriteTimeUtc(string path, DateTime utc) => throw new NotSupportedException();
    public void Replace(string sourcePath, string destinationPath) => throw new NotSupportedException();
    public long GetAvailableFreeSpace(string path) => long.MaxValue;
}
