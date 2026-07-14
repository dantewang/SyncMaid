using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using SyncMaid.Lang;

namespace SyncMaid.Services;

/// <summary>
/// The runtime language switch. All UI strings resolve against
/// <see cref="CultureInfo.CurrentUICulture"/>; <see cref="Apply"/> changes it and then
/// broadcasts the change two ways so the whole UI re-renders without a restart:
/// <see cref="INotifyPropertyChanged"/> on the indexer for XAML bindings (every
/// <c>{l:Loc}</c> binds to <see cref="this[string]"/> on <see cref="Instance"/>), and a
/// <see cref="CultureChangedMessage"/> for view models, whose computed string properties
/// re-evaluate via <c>ViewModelBase.OnCultureChanged</c>. A singleton rather than a DI
/// service because XAML markup extensions resolve at parse time, outside the container.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    /// <summary>The OS-default UI culture, captured before any <see cref="Apply"/> so the
    /// "system default" language option can restore it within the session.</summary>
    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;

    public static Localizer Instance { get; } = new();

    private Localizer()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Looks up a resource by key in the current UI culture. Falls back to the
    /// key itself for an unknown key, so a lookup never throws or returns null.</summary>
    public string this[string key] =>
        Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>Formats a composite-format resource (pass a <c>Strings.*Format</c>
    /// property, keeping the key compile-checked) with the current formatting culture.</summary>
    public static string Format(string compositeFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, compositeFormat, args);

    /// <summary>Count-aware lookup: formats <c>&lt;baseKey&gt;.One</c> when
    /// <paramref name="count"/> is 1, else <c>&lt;baseKey&gt;.Other</c> — right for
    /// English, harmless for languages without plural inflection.</summary>
    public static string Plural(string baseKey, int count) =>
        Format(Instance[count == 1 ? baseKey + ".One" : baseKey + ".Other"], count);

    /// <summary>
    /// Switches the UI culture: a BCP-47 tag (e.g. "zh-Hans"), or null/empty for the OS
    /// default. Only <c>CurrentUICulture</c> changes — dates and numbers keep following
    /// the OS regional settings via <c>CurrentCulture</c>.
    /// </summary>
    public void Apply(string? cultureTag)
    {
        CultureInfo culture;
        try
        {
            culture = string.IsNullOrEmpty(cultureTag)
                ? SystemUiCulture
                : CultureInfo.GetCultureInfo(cultureTag);
        }
        catch (CultureNotFoundException)
        {
            // A malformed tag from a hand-edited settings.json must not block startup.
            culture = SystemUiCulture;
        }
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // "Item" is the property name XAML `{l:Loc}` bindings subscribe to (the indexer).
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        WeakReferenceMessenger.Default.Send(new CultureChangedMessage());
    }
}
