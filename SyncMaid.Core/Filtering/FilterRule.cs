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
[JsonDerivedType(typeof(WildcardFilter), "wildcard")]
[JsonDerivedType(typeof(AllOfFilter), "allOf")]
[JsonDerivedType(typeof(AnyOfFilter), "anyOf")]
[JsonDerivedType(typeof(NotFilter), "not")]
public abstract record FilterRule
{
    /// <summary>Returns <c>true</c> when <paramref name="relativePath"/> is selected by this rule.</summary>
    public abstract bool Matches(string relativePath);
}

/// <summary>Selects every file under the source (the original requirements' "all files, no rule").</summary>
public sealed record AllFilesFilter : FilterRule
{
    public override bool Matches(string relativePath) => true;
}

/// <summary>Selects files that live at or under a specific relative path of the source.</summary>
public sealed record PathFilter : FilterRule
{
    private readonly string _matchPrefix;

    public PathFilter(string prefix)
    {
        Prefix = prefix.Replace('\\', '/').Trim('/');
        _matchPrefix = Prefix + '/';
    }

    public string Prefix { get; init; }

    public override bool Matches(string relativePath)
    {
        var path = relativePath.AsSpan();
        while (path.Length > 0 && IsSeparator(path[0]))
        {
            path = path[1..];
        }

        // Selecting everything is represented explicitly by AllFilesFilter. An empty
        // normalized path (including slash-only persisted input) therefore selects nothing.
        return Prefix.Length > 0
               && (EqualsNormalized(path, Prefix)
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

/// <summary>
/// Selects files whose source-relative path matches a wildcard pattern: <c>*</c> for any run
/// of characters inside one path segment, <c>?</c> for one such character, and <c>**</c> for
/// any number of segments — so <c>**/ChatGPT*.png</c> finds those files at any depth, the
/// source root included. Case-insensitive, and backslashes normalize to forward slashes, like
/// the path and extension rules.
/// </summary>
public sealed record WildcardFilter : FilterRule
{
    private readonly string[] _patternSegments;

    public WildcardFilter(string pattern)
    {
        Pattern = pattern.Replace('\\', '/').Trim('/');
        _patternSegments = Pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    public string Pattern { get; init; }

    // Structural equality: the compiler-generated version also compares the cached segment
    // array, which is by reference — two filters built from the same pattern would come out
    // unequal, breaking record value semantics (and every round-trip assertion).
    public bool Equals(WildcardFilter? other) =>
        other is not null && string.Equals(Pattern, other.Pattern, StringComparison.Ordinal);

    public override int GetHashCode() => Pattern.GetHashCode();

    public override bool Matches(string relativePath)
    {
        // An empty pattern selects nothing: selecting everything is AllFilesFilter's job,
        // the same convention PathFilter follows.
        if (_patternSegments.Length == 0)
        {
            return false;
        }

        var pathSegments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return MatchSegments(_patternSegments, pathSegments);
    }

    // Segment-wise wildcard matching with one backtrack point, the classic linear-time
    // algorithm lifted a level: "**" plays the part "*" plays inside a segment. Doing it by
    // hand rather than translating to a regex keeps the AOT build free of a regex dependency
    // and cannot backtrack pathologically on a deep path.
    private static bool MatchSegments(string[] pattern, string[] path)
    {
        int patternIndex = 0, pathIndex = 0, starIndex = -1, starPathIndex = 0;
        while (pathIndex < path.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == "**")
            {
                // Remember where to resume, then try consuming no segments at all.
                starIndex = patternIndex++;
                starPathIndex = pathIndex;
            }
            else if (patternIndex < pattern.Length
                     && MatchSegment(pattern[patternIndex], path[pathIndex]))
            {
                patternIndex++;
                pathIndex++;
            }
            else if (starIndex >= 0)
            {
                // Give the "**" one more segment and retry from just after it.
                patternIndex = starIndex + 1;
                pathIndex = ++starPathIndex;
            }
            else
            {
                return false;
            }
        }

        // A trailing "**" may match nothing at all ("a/**" matches "a").
        while (patternIndex < pattern.Length && pattern[patternIndex] == "**")
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    // "*" and "?" within one segment; neither ever crosses a separator, because the
    // separators were split away before this is called.
    private static bool MatchSegment(string pattern, string text)
    {
        int patternIndex = 0, textIndex = 0, starIndex = -1, starTextIndex = 0;
        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                starTextIndex = textIndex;
            }
            else if (patternIndex < pattern.Length
                     && (pattern[patternIndex] == '?'
                         || char.ToUpperInvariant(pattern[patternIndex])
                            == char.ToUpperInvariant(text[textIndex])))
            {
                patternIndex++;
                textIndex++;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++starTextIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
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
