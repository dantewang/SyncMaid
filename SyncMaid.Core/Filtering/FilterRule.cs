namespace SyncMaid.Core.Filtering;

/// <summary>
/// A single rule deciding whether a source file — identified by its path
/// relative to the source root — should be synced to a destination.
/// </summary>
/// <remarks>
/// Closed hierarchy: callers pattern-match the concrete types exhaustively,
/// so no reflection is involved and the model stays AOT/trim-safe.
/// </remarks>
public abstract record FilterRule
{
    /// <summary>Returns <c>true</c> when <paramref name="relativePath"/> is selected by this rule.</summary>
    public abstract bool Matches(string relativePath);

    /// <summary>Normalizes a relative path to forward slashes with no leading separator.</summary>
    private protected static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}

/// <summary>Selects every file under the source (the design doc's "all files, no rule").</summary>
public sealed record AllFilesFilter : FilterRule
{
    public override bool Matches(string relativePath) => true;
}

/// <summary>Selects files that live at or under a specific relative path of the source.</summary>
public sealed record PathFilter(string Prefix) : FilterRule
{
    public override bool Matches(string relativePath)
    {
        var path = Normalize(relativePath);
        var prefix = Normalize(Prefix);

        return prefix.Length == 0
               || path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(prefix + '/', StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Selects files of a specific type by extension, e.g. <c>"jpg"</c> or <c>".jpg"</c>.</summary>
public sealed record ExtensionFilter(string Extension) : FilterRule
{
    public override bool Matches(string relativePath)
    {
        var ext = Extension.TrimStart('.');
        return ext.Length > 0
               && relativePath.EndsWith('.' + ext, StringComparison.OrdinalIgnoreCase);
    }
}
