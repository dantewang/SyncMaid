using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// One destination as a row of the task workspace: a one-line summary that expands in place
/// into the full editor. The row owns the edit — the editor is built when it opens and
/// dropped when it closes — so the workspace holds nothing half-edited.
/// </summary>
public sealed partial class DestinationRowViewModel : ViewModelBase
{
    private readonly Func<DestinationRowViewModel, DestinationEditorViewModel> _newEditor;
    private readonly Action<DestinationRowViewModel, Destination?> _onEditorClosed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    private DestinationEditorViewModel? _editor;

    /// <summary>1-based position, shown in the row and meaningful for a Move task, where it
    /// is the order rules are matched in. Refreshed by the workspace after a reorder.</summary>
    [ObservableProperty]
    private int _number;

    /// <summary>A rule that never matched anything, because an earlier one takes everything
    /// it would; null when the rule is reachable. Advisory — it never blocks saving.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShadowed))]
    private string? _shadowedBy;

    /// <summary>How many of the source's files this destination would take, from the last
    /// preview scan; null until one has run (and cleared whenever the rules change).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string? _previewCount;

    /// <summary>A few of the files behind <see cref="PreviewCount"/>, as the row's tooltip —
    /// a count alone does not tell you whether it caught the right things.</summary>
    [ObservableProperty]
    private string? _previewSample;

    /// <summary>True once a preview scan has produced a count for this row.</summary>
    public bool HasPreview => PreviewCount is not null;

    public DestinationRowViewModel(
        Destination destination,
        Func<DestinationRowViewModel, DestinationEditorViewModel> newEditor,
        Action<DestinationRowViewModel, Destination?> onEditorClosed)
    {
        Destination = destination;
        _newEditor = newEditor;
        _onEditorClosed = onEditorClosed;
        // A row that has never been through the editor holds nothing worth keeping; the
        // workspace drops it if the user backs out.
        IsDraft = string.IsNullOrEmpty(destination.LocalPath);
    }

    /// <summary>The saved destination. Replaced when an edit is applied.</summary>
    public Destination Destination { get; private set; }

    /// <summary>True until an edit is applied: the row exists but the destination does not.</summary>
    public bool IsDraft { get; private set; }

    /// <summary>True while the row shows its summary rather than the editor.</summary>
    public bool IsCollapsed => Editor is null;

    public string Name => Destination.Name;

    public string Path => Destination.LocalPath;

    /// <summary>What this destination selects, in one line: the catch-all says so in words,
    /// everything else describes its filters.</summary>
    public string Summary => IsCatchAll
        ? Strings.Workspace_EverythingElse
        : Destination.Filters is [AllFilesFilter]
            ? Strings.Filter_AllFiles
            : FilterDescriber.Describe(Destination.Filters);

    /// <summary>
    /// True for the "everything else" rule: a Move destination taking all files. Under
    /// first-match-wins it collects whatever the rules above it left, which is only
    /// meaningful at the end of the list — so it is pinned there and never reordered.
    /// </summary>
    public bool IsCatchAll =>
        Destination.Strategy == SyncStrategy.Move && Destination.Filters is [AllFilesFilter];

    /// <summary>True when this rule can never match anything; drives the row's warning.</summary>
    public bool IsShadowed => ShadowedBy is not null;

    /// <summary>Opens the editor in place. Idempotent — reopening keeps the pending edit.</summary>
    [RelayCommand]
    public void Expand()
    {
        if (Editor is not null)
        {
            return;
        }

        var editor = _newEditor(this);
        editor.CloseRequested += OnEditorClosed;
        Editor = editor;
    }

    /// <summary>Discards any pending edit and shows the summary again.</summary>
    [RelayCommand]
    private void Collapse() => Editor?.RequestCancel();

    /// <summary>
    /// Adds a rule for one of the file types actually present in the source (the chips the
    /// preview scan found), turning glob authoring into picking. No-op unless this row's
    /// editor is open — the chips are only shown there.
    /// </summary>
    [RelayCommand]
    private void AddExtension(string? extension)
    {
        if (Editor is not { } editor
            || string.IsNullOrWhiteSpace(extension)
            || editor.Groups.FirstOrDefault() is not { } group)
        {
            return;
        }

        // Picking a type is a file selection, so "all files" cannot still be the answer.
        editor.SyncAll = false;
        group.SelectedFilterKind = FilterKind.Extension;
        group.NewFilterPattern = extension;
        group.AddRuleCommand.Execute(null);
    }

    private void OnEditorClosed(Destination? edited)
    {
        if (Editor is { } editor)
        {
            editor.CloseRequested -= OnEditorClosed;
        }

        Editor = null;
        if (edited is not null)
        {
            Destination = edited;
            IsDraft = false;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Path));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(IsCatchAll));
        }

        _onEditorClosed(this, edited);
    }
}
