using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;
using SyncMaid.Core.Model;

namespace SyncMaid.Converters;

/// <summary>Maps a <see cref="SyncOutcome"/> to the Material icon shown next to the status.</summary>
public sealed class SyncOutcomeToIconKindConverter : IValueConverter
{
    public static readonly SyncOutcomeToIconKindConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SyncOutcome outcome
            ? outcome switch
            {
                SyncOutcome.Success => MaterialIconKind.CheckCircle,
                SyncOutcome.Failed => MaterialIconKind.AlertCircle,
                SyncOutcome.Running => MaterialIconKind.Sync,
                _ => MaterialIconKind.MinusCircle,
            }
            : MaterialIconKind.MinusCircle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
