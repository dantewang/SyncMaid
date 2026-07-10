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
}

/// <summary>Selects every file under the source (the design doc's "all files, no rule").</summary>
public sealed record AllFilesFilter : FilterRule
{
    public override bool Matches(string relativePath) => true;
}

/// <summary>Selects files that live at or under a specific relative path of the source.</summary>
public sealed record PathFilter : FilterRule
{
    private readonly string _matchPrefix;
    private readonly bool _matchesNothing;

    public PathFilter(string prefix)
    {
        Prefix = prefix.Replace('\\', '/').Trim('/');
        _matchPrefix = Prefix + '/';
        _matchesNothing = prefix.Length > 0 && Prefix.Length == 0;
    }

    public string Prefix { get; init; }

    public override bool Matches(string relativePath)
    {
        var path = relativePath.AsSpan();
        while (path.Length > 0 && IsSeparator(path[0]))
        {
            path = path[1..];
        }

        return !_matchesNothing
               && (Prefix.Length == 0
               || EqualsNormalized(path, Prefix)
               || (path.Length >= _matchPrefix.Length
                   && EqualsNormalized(path[.._matchPrefix.Length], _matchPrefix)));
    }

    private static bool EqualsNormalized(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (IsSeparator(left[i]) && IsSeparator(right[i]))
            {
                continue;
            }

            if (char.ToUpperInvariant(left[i]) != char.ToUpperInvariant(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSeparator(char value) => value is '/' or '\\';
}

/// <summary>Selects files of a specific type by extension, e.g. <c>"jpg"</c> or <c>".jpg"</c>.</summary>
public sealed record ExtensionFilter : FilterRule
{
    private readonly string _matchSuffix;

    public ExtensionFilter(string extension)
    {
        Extension = extension.TrimStart('*').TrimStart('.');
        _matchSuffix = '.' + Extension;
    }

    public string Extension { get; init; }

    public override bool Matches(string relativePath) =>
        Extension.Length > 0
        && relativePath.EndsWith(_matchSuffix, StringComparison.OrdinalIgnoreCase);
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
