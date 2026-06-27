using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace SyncMaid.Converters;

/// <summary>Maps an expanded flag to the chevron icon: down when expanded, right when collapsed.</summary>
public sealed class BoolToChevronKindConverter : IValueConverter
{
    public static readonly BoolToChevronKindConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
