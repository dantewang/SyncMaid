using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Model;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Backs the independent mirror-delete confirmation window. Raises <see cref="Decided"/>
/// with the user's choice; the window host closes on it. Derives from
/// <see cref="ViewModelBase"/> (not <c>ObservableObject</c> directly) because this window
/// can be open while the language switches — the base's culture hook re-renders it.
/// </summary>
public partial class ConfirmMirrorDeleteViewModel : ViewModelBase
{
    private readonly bool _recycle;

    public ConfirmMirrorDeleteViewModel(MirrorDeleteRequest request)
    {
        DestinationName = request.DestinationName;
        DestinationPath = request.DestinationPath;
        Count = request.Preview.Count;
        Sample = request.Preview.Sample;
        _recycle = request.DeleteMode == DeleteMode.Recycle;
    }

    public string DestinationName { get; }
    public string DestinationPath { get; }
    public int Count { get; }
    public IReadOnlyList<string> Sample { get; }

    public string Explanation => Localizer.Format(
        _recycle ? Strings.MirrorDelete_ExplanationRecycleFormat : Strings.MirrorDelete_ExplanationPermanentFormat,
        DestinationName, Count);

    public string ConfirmLabel => _recycle
        ? Strings.MirrorDelete_MoveToRecycleBin
        : Localizer.Plural("MirrorDelete.DeleteCount", Count);

    public bool HasMore => Count > Sample.Count;

    public string MoreText => HasMore
        ? Localizer.Format(Strings.MirrorDelete_MoreFormat, Count - Sample.Count)
        : string.Empty;

    /// <summary>Raised with the user's decision: true to delete, false to keep.</summary>
    public event Action<bool>? Decided;

    [RelayCommand]
    private void Delete() => Decided?.Invoke(true);

    [RelayCommand]
    private void Keep() => Decided?.Invoke(false);
}
