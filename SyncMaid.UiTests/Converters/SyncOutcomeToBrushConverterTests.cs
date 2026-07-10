using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SyncMaid.Converters;
using SyncMaid.Core.Model;

namespace SyncMaid.UiTests.Converters;

public class SyncOutcomeToBrushConverterTests
{
    [AvaloniaFact]
    public void Outcomes_resolve_the_shared_application_palette_brushes()
    {
        var mappings = new[]
        {
            (SyncOutcome.Success, "SuccessBrush"),
            (SyncOutcome.Running, "SuccessBrush"),
            (SyncOutcome.Failed, "DangerBrush"),
            (SyncOutcome.NeedsConfirmation, "WarningBrush"),
            (SyncOutcome.Never, "TextMutedBrush"),
        };

        foreach (var (outcome, resourceKey) in mappings)
        {
            Assert.True(Application.Current!.TryGetResource(
                resourceKey,
                Application.Current.ActualThemeVariant,
                out var expected));

            var actual = SyncOutcomeToBrushConverter.Instance.Convert(
                outcome,
                typeof(IBrush),
                parameter: null,
                CultureInfo.InvariantCulture);

            Assert.Same(expected, actual);
        }
    }
}
