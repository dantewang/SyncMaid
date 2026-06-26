using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace SyncMaid.Converters;

/// <summary>
/// Two-way converter for binding a radio/segment's <c>IsChecked</c> to an enum property:
/// checked when the value equals the converter parameter, and writes the parameter back
/// when checked. The parameter is passed as the boxed enum via <c>x:Static</c>, so there
/// is no parsing or reflection (AOT-safe).
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public static readonly EnumMatchConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.Equals(parameter) ?? false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : BindingOperations.DoNothing;
}
