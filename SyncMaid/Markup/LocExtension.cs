using System;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using SyncMaid.Services;

namespace SyncMaid.Markup;

/// <summary>
/// <c>{l:Loc Some.Key}</c> — binds any XAML property to the localized string for the key,
/// live: when <see cref="Localizer.Apply"/> switches the UI culture, every bound value
/// updates in place. Builds a compiled binding to <see cref="Localizer.Instance"/>'s
/// indexer from explicit delegates — no reflection, so it satisfies the app's
/// warning-free AOT bar — and carries its own <c>Source</c>, so it works on any target
/// (tooltips, placeholder text, window titles, native menu items) regardless of
/// DataContext. This one imperative helper is what keeps every localized call site
/// declarative XAML.
/// </summary>
public sealed class LocExtension
{
    public LocExtension(string key) => Key = key;

    /// <summary>The resource key, e.g. <c>Main.RunAll</c>.</summary>
    public string Key { get; }

    public CompiledBinding ProvideValue(IServiceProvider serviceProvider)
    {
        var key = Key;
        var path = new CompiledBindingPathBuilder()
            .Property(
                // "Item" is the CLR name of the indexer; Localizer raises PropertyChanged
                // with exactly this name when the culture changes.
                new ClrPropertyInfo(
                    "Item",
                    instance => ((Localizer)instance)[key],
                    setter: null,
                    typeof(string)),
                PropertyInfoAccessorFactory.CreateInpcPropertyAccessor)
            .Build();

        var binding = new CompiledBindingExtension(path)
        {
            Source = Localizer.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
