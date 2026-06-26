using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncMaid.ViewModels;

/// <summary>
/// Base for all view models. <see cref="ObservableObject"/> supplies
/// <c>INotifyPropertyChanged</c> via the MVVM Toolkit's source generators — no
/// reflection, so the app stays AOT/trim-friendly.
/// </summary>
public abstract class ViewModelBase : ObservableObject;
