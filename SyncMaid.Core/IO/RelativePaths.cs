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

    /// <summary>Returns whether two paths identify the same Windows filesystem location.</summary>
    public static bool AreEquivalent(string first, string second) =>
        string.Equals(NormalizeFullPath(first), NormalizeFullPath(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns whether <paramref name="candidate"/> is nested beneath <paramref name="root"/>.</summary>
    public static bool IsDescendantOf(string candidate, string root)
    {
        var normalizedRoot = NormalizeFullPath(root);
        if (!Path.EndsInDirectorySeparator(normalizedRoot))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return NormalizeFullPath(candidate).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFullPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
