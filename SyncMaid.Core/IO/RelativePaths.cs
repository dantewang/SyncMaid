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

    /// <summary>
    /// Returns whether two folder paths identify overlapping trees on a Windows
    /// filesystem: the same location, or one nested beneath the other, in either
    /// direction. This is the path relation behind the task shape conventions
    /// (AGENT.md). Null, whitespace, or unresolvable input (e.g. a partially typed
    /// UNC prefix) overlaps nothing.
    /// </summary>
    public static bool Overlaps(string? first, string? second)
    {
        var normalizedFirst = TryNormalizeFullPath(first);
        var normalizedSecond = TryNormalizeFullPath(second);
        if (normalizedFirst is null || normalizedSecond is null)
        {
            return false;
        }

        return string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase)
            || normalizedFirst.StartsWith(WithTrailingSeparator(normalizedSecond), StringComparison.OrdinalIgnoreCase)
            || normalizedSecond.StartsWith(WithTrailingSeparator(normalizedFirst), StringComparison.OrdinalIgnoreCase);
    }

    private static string WithTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    // Callers evaluate user input as it is typed ("\\", "\\server"), which GetFullPath
    // rejects; an unresolvable path simply has no filesystem location to relate.
    private static string? TryNormalizeFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

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
