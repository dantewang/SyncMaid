namespace SyncMaid.Core.IO;

/// <summary>Operations shared by paths stored relative to a configured root.</summary>
public static class RelativePaths
{
    /// <summary>Joins a root with a forward-slash relative path.</summary>
    public static string Join(string root, string relativePath)
    {
        var trimmedRoot = root.TrimEnd('/', '\\');
        return $"{trimmedRoot}/{relativePath}";
    }

    /// <summary>Returns whether two paths identify the same Windows filesystem location.
    /// False when either path cannot be resolved (e.g. a partially typed UNC prefix).</summary>
    public static bool AreEquivalent(string first, string second)
    {
        var normalizedFirst = TryNormalizeFullPath(first);
        var normalizedSecond = TryNormalizeFullPath(second);
        return normalizedFirst is not null
            && string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns whether <paramref name="candidate"/> is nested beneath <paramref name="root"/>.
    /// False when either path cannot be resolved (e.g. a partially typed UNC prefix).</summary>
    public static bool IsDescendantOf(string candidate, string root)
    {
        var normalizedCandidate = TryNormalizeFullPath(candidate);
        var normalizedRoot = TryNormalizeFullPath(root);
        if (normalizedCandidate is null || normalizedRoot is null)
        {
            return false;
        }

        return normalizedCandidate.StartsWith(WithTrailingSeparator(normalizedRoot), StringComparison.OrdinalIgnoreCase);
    }

    private static string WithTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    // Callers evaluate user input as it is typed ("\\", "\\server"), which GetFullPath
    // rejects; an unresolvable path simply has no filesystem location to relate.
    private static string? TryNormalizeFullPath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or PathTooLongException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
