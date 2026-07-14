using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SyncMaid.Lang;
using SyncMaid.ViewModels;

namespace SyncMaid.Converters;

/// <summary>
/// Maps a <see cref="FilterKind"/> to its localized display name for the filter-kind
/// ComboBox (which would otherwise render the raw enum name via ToString). The open
/// dropdown doesn't hot-switch, which is fine: the destination editor and the Settings
/// dialog share the single modal host, so a language can't change while it is open.
/// </summary>
public sealed class FilterKindToTextConverter : IValueConverter
{
    public static readonly FilterKindToTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            FilterKind.Path => Strings.Enum_FilterKind_Path,
            FilterKind.Extension => Strings.Enum_FilterKind_Extension,
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
