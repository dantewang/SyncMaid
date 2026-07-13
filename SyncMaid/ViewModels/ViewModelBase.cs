using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Base for all view models. <see cref="ObservableObject"/> supplies
/// <c>INotifyPropertyChanged</c> via the MVVM Toolkit's source generators — no
/// reflection, so the app stays AOT/trim-friendly. Every view model also listens for
/// runtime language switches (weakly, so transient instances need no unsubscribe
/// bookkeeping) and refreshes its bound properties, which re-evaluates the many
/// computed display strings in the new language.
/// </summary>
public abstract class ViewModelBase : ObservableObject, IRecipient<CultureChangedMessage>
{
    protected ViewModelBase()
    {
        WeakReferenceMessenger.Default.Register(this);
    }

    void IRecipient<CultureChangedMessage>.Receive(CultureChangedMessage message) => OnCultureChanged();

    /// <summary>Invalidates every bound property — an empty property name means "all
    /// properties changed" to <c>INotifyPropertyChanged</c> consumers, Avalonia included.</summary>
    protected virtual void OnCultureChanged() => OnPropertyChanged(string.Empty);
}
