using Avalonia;
using Avalonia.Controls.Primitives;
using Material.Icons;

namespace SyncMaid.Controls;

/// <summary>
/// The shared notice row — an icon beside a text on the subtle rounded chrome — used for
/// path hints, warnings, and previews. A templated control so the wrap-safe layout lives in
/// exactly one template (an Auto,* grid bounds the text's width, which is what makes
/// TextWrapping engage); the ad-hoc horizontal StackPanels it replaces measured the text
/// with unbounded width, so long messages ran out of the box. Severity is a style class
/// ("warning", "danger"; default muted) that colours the icon via Foreground — see
/// Styles/Theme.axaml for the template.
/// </summary>
public sealed class HintBox : TemplatedControl
{
    /// <summary>The icon shown at the left edge; alert by default.</summary>
    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<HintBox, MaterialIconKind>(nameof(Icon), MaterialIconKind.AlertOutline);

    /// <summary>The notice text; wraps within the box.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HintBox, string?>(nameof(Text));

    public MaterialIconKind Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
