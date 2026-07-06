using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Model;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Backs the independent mirror-delete confirmation window. Raises <see cref="Decided"/>
/// with the user's choice; the window host closes on it.
/// </summary>
public partial class ConfirmMirrorDeleteViewModel : ObservableObject
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

    public string Explanation =>
        _recycle
            ? $"Syncing “{DestinationName}” will move {Count} files to the Recycle Bin — they are no longer in the source."
            : $"Syncing “{DestinationName}” will permanently delete {Count} files that are no longer in the source.";

    public string ConfirmLabel => _recycle ? "Move to Recycle Bin" : $"Delete {Count} files";

    public bool HasMore => Count > Sample.Count;

    public string MoreText => HasMore ? $"…and {Count - Sample.Count} more" : string.Empty;

    /// <summary>Raised with the user's decision: true to delete, false to keep.</summary>
    public event Action<bool>? Decided;

    [RelayCommand]
    private void Delete() => Decided?.Invoke(true);

    [RelayCommand]
    private void Keep() => Decided?.Invoke(false);
}
