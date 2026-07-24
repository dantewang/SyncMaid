using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SyncMaid.Core.Model;

namespace SyncMaid.Converters;

/// <summary>Maps a <see cref="SyncOutcome"/> to the brush used for status text.</summary>
public sealed class SyncOutcomeToBrushConverter : IValueConverter
{
    public static readonly SyncOutcomeToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = value is SyncOutcome outcome
            ? outcome switch
            {
                SyncOutcome.Success or SyncOutcome.Running => "SuccessBrush",
                SyncOutcome.Failed => "DangerBrush",
                SyncOutcome.NeedsConfirmation or SyncOutcome.Incomplete => "WarningBrush",
                _ => "TextMutedBrush",
            }
            : "TextMutedBrush";

        var application = Application.Current;
        return application is not null
               && application.TryGetResource(resourceKey, application.ActualThemeVariant, out var resource)
               && resource is IBrush
            ? resource
            : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
