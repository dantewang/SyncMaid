using System.Text.Json.Serialization;

namespace SyncMaid.Core.Filtering;

/// <summary>
/// A single rule deciding whether a source file — identified by its path
/// relative to the source root — should be synced to a destination.
/// </summary>
/// <remarks>
/// Closed hierarchy: callers pattern-match the concrete types exhaustively,
/// so no reflection is involved and the model stays AOT/trim-safe. The JSON
/// discriminators below let the source-generated serializer persist the concrete
/// type without reflection.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AllFilesFilter), "all")]
[JsonDerivedType(typeof(PathFilter), "path")]
[JsonDerivedType(typeof(ExtensionFilter), "extension")]
[JsonDerivedType(typeof(AllOfFilter), "allOf")]
[JsonDerivedType(typeof(AnyOfFilter), "anyOf")]
[JsonDerivedType(typeof(NotFilter), "not")]
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

/// <summary>Selects files matching <b>every</b> child rule (AND). Empty matches nothing —
/// an accidental empty conjunction must not silently select the whole source.</summary>
public sealed record AllOfFilter(IReadOnlyList<FilterRule> Rules) : FilterRule
{
    public override bool Matches(string relativePath) =>
        Rules.Count > 0 && Rules.All(rule => rule.Matches(relativePath));

    // Structural equality: the compiler-generated implementation compares the list by
    // reference, which breaks record value semantics (and round-trip assertions).
    public bool Equals(AllOfFilter? other) => other is not null && Rules.SequenceEqual(other.Rules);

    public override int GetHashCode() => Rules.Aggregate(typeof(AllOfFilter).GetHashCode(), HashCode.Combine);
}

/// <summary>Selects files matching <b>any</b> child rule (OR). Empty matches nothing —
/// the same convention as an empty <see cref="Model.Destination.Filters"/> list.</summary>
public sealed record AnyOfFilter(IReadOnlyList<FilterRule> Rules) : FilterRule
{
    public override bool Matches(string relativePath) =>
        Rules.Any(rule => rule.Matches(relativePath));

    public bool Equals(AnyOfFilter? other) => other is not null && Rules.SequenceEqual(other.Rules);

    public override int GetHashCode() => Rules.Aggregate(typeof(AnyOfFilter).GetHashCode(), HashCode.Combine);
}

/// <summary>Selects files <b>not</b> matched by the wrapped rule ("everything except…").</summary>
public sealed record NotFilter(FilterRule Rule) : FilterRule
{
    public override bool Matches(string relativePath) => !Rule.Matches(relativePath);
}
