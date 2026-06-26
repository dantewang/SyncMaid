using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SyncMaid.Core.Model;

namespace SyncMaid.Converters;

/// <summary>Maps a <see cref="SyncOutcome"/> to the brush used for status text.</summary>
public sealed class SyncOutcomeToBrushConverter : IValueConverter
{
    public static readonly SyncOutcomeToBrushConverter Instance = new();

    private static readonly IBrush Success = new SolidColorBrush(Color.Parse("#2E9E6B"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#C53943"));
    private static readonly IBrush Running = new SolidColorBrush(Color.Parse("#1D9E75"));
    private static readonly IBrush Never = new SolidColorBrush(Color.Parse("#8A8A86"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SyncOutcome outcome
            ? outcome switch
            {
                SyncOutcome.Success => Success,
                SyncOutcome.Failed => Failed,
                SyncOutcome.Running => Running,
                _ => Never,
            }
            : Never;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
