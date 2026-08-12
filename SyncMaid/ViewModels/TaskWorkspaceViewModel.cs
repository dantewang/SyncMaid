using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Every destination of one task, in one place. A routing task is a rule set, and a rule set
/// can only be judged as a whole — which rule catches a given file, which one wins when two
/// match, what nothing matches — so the rules are listed together and edited in place rather
/// than one modal at a time.
/// </summary>
public sealed partial class TaskWorkspaceViewModel : DialogViewModel<IReadOnlyList<Destination>>
{
    private readonly SyncTask _task;
    private readonly IFolderPickerService _folderPicker;
    private readonly Func<string, string?> _crossTaskConflicts;

    public TaskWorkspaceViewModel(
        SyncTask task,
        IFolderPickerService folderPicker,
        Func<string, string?>? crossTaskConflicts = null,
        Guid? expand = null,
        bool startWithNewRule = false)
    {
        _task = task;
        _folderPicker = folderPicker;
        _crossTaskConflicts = crossTaskConflicts ?? (_ => null);

        Rows = new ObservableCollection<DestinationRowViewModel>(
            task.Destinations.Select(NewRow));
        Rows.CollectionChanged += (_, _) => Renumber();
        Renumber();

        // Opened from a row's edit button: that row starts open, so the click lands where
        // the user aimed it instead of on a list they then have to search.
        if (expand is { } id)
        {
            Rows.FirstOrDefault(row => row.Destination.Id == id)?.Expand();
        }

        if (startWithNewRule)
        {
            AddRule();
        }
    }

    /// <summary>The task's destinations, in order. For a Move task the order is the matching
    /// order, so it is part of what is being edited.</summary>
    public ObservableCollection<DestinationRowViewModel> Rows { get; }

    public string TaskName => _task.Name;

    public string SourcePath => _task.SourcePath;

    /// <summary>True when the destinations are an ordered rule list rather than independent
    /// sync targets: numbering, reordering and the catch-all only mean something here.</summary>
    public bool IsRouting => _task.Kind == SyncTaskKind.Move;

    public string Title => IsRouting ? Strings.Workspace_RulesTitle : Strings.Workspace_DestinationsTitle;

    /// <summary>The line under the title explaining how the list is read.</summary>
    public string Subtitle => IsRouting
        ? Strings.Workspace_RoutingSubtitle
        : Strings.Workspace_SyncSubtitle;

    /// <summary>True when the task has no destinations at all — the empty state.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// True when a Move task has no "everything else" rule, so files nothing matches stay in
    /// the source. That is a legitimate choice — the catch-all is added deliberately, never
    /// conjured — so this only decides whether the command is offered.
    /// </summary>
    public bool CanAddCatchAll => IsRouting && !Rows.Any(row => row.IsCatchAll);

    [RelayCommand]
    private void AddRule()
    {
        var row = NewRow(new Destination(
            string.Empty,
            string.Empty,
            [],
            IsRouting ? SyncStrategy.Move : SyncStrategy.Mirror));

        Insert(row);
        row.Expand();
    }

    /// <summary>
    /// Adds the "everything else" rule: an all-files Move destination, which under
    /// first-match-wins takes exactly what the rules above it left. It only means that at the
    /// end of the list, so it goes last and stays there.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddCatchAll))]
    private void AddCatchAll()
    {
        var row = NewRow(new Destination(
            Strings.Workspace_EverythingElse, string.Empty, [new AllFilesFilter()], SyncStrategy.Move));

        Rows.Add(row);
        row.Expand();
    }

    [RelayCommand]
    private void Duplicate(DestinationRowViewModel row)
    {
        // A new id: the copy is a different destination, and sharing one would make both
        // rows report the same last-run status.
        var copy = row.Destination with { Id = Guid.NewGuid() };
        var added = NewRow(copy);
        Rows.Insert(IndexBefore(row) + 1, added);
        added.Expand();
    }

    [RelayCommand]
    private void Delete(DestinationRowViewModel row) => Rows.Remove(row);

    [RelayCommand]
    private void MoveUp(DestinationRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index > 0 && !row.IsCatchAll)
        {
            Rows.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(DestinationRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        // The catch-all stays last: a rule below "everything else" could never match.
        if (index >= 0 && index + 1 < Rows.Count && !row.IsCatchAll && !Rows[index + 1].IsCatchAll)
        {
            Rows.Move(index, index + 1);
        }
    }

    // A row whose editor was never accepted has no destination behind it, so saving with one
    // still open must not persist a nameless, pathless rule.
    [RelayCommand]
    private void Save() =>
        Close(Rows.Where(row => !row.IsDraft).Select(row => row.Destination).ToList());

    [RelayCommand]
    private void Cancel() => RequestCancel();

    private void Insert(DestinationRowViewModel row)
    {
        var catchAll = Rows.FirstOrDefault(existing => existing.IsCatchAll);
        Rows.Insert(catchAll is null ? Rows.Count : Rows.IndexOf(catchAll), row);
    }

    private int IndexBefore(DestinationRowViewModel row) => Rows.IndexOf(row);

    private DestinationRowViewModel NewRow(Destination destination) =>
        new(destination, NewEditor, OnEditorClosed);

    private DestinationEditorViewModel NewEditor(DestinationRowViewModel row) =>
        new(
            _folderPicker,
            // A blank destination is a row being created, not one being edited: the editor
            // starts empty rather than showing an unnamed, pathless "existing" destination.
            string.IsNullOrEmpty(row.Destination.LocalPath) && row.Destination.Filters.Count == 0
                ? null
                : row.Destination,
            _task.SourcePath,
            taskKind: _task.Kind,
            destinationConflicts: path => FindConflict(row, path),
            isCatchAll: row.IsCatchAll);

    // Task shape convention (AGENT.md): destinations never overlap — the rows beside this
    // one, and every other task's destinations.
    private DestinationConflict? FindConflict(DestinationRowViewModel row, string path)
    {
        var sibling = Rows.FirstOrDefault(other =>
            !ReferenceEquals(other, row) && RelativePaths.Overlaps(other.Path, path));
        if (sibling is not null)
        {
            return new DestinationConflict(sibling.Name, WithinTask: true);
        }

        return _crossTaskConflicts(path) is { } task
            ? new DestinationConflict(task, WithinTask: false)
            : null;
    }

    private void OnEditorClosed(DestinationRowViewModel row, Destination? edited)
    {
        // Backing out of a rule that was never saved leaves nothing behind — the row was
        // only ever the editor's frame.
        if (edited is null)
        {
            if (row.IsDraft)
            {
                Rows.Remove(row);
            }

            return;
        }

        // An "everything else" rule that was just created has to move to the end, the only
        // place it means anything.
        if (row.IsCatchAll && Rows.IndexOf(row) is var index && index >= 0 && index != Rows.Count - 1)
        {
            Rows.Move(index, Rows.Count - 1);
        }

        Renumber();
    }

    // Numbers are positions, so they change whenever the list does; the shadowed-rule
    // warnings are recomputed with them since they depend on the same order.
    private void Renumber()
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].Number = i + 1;
            Rows[i].ShadowedBy = IsRouting ? ShadowedBy(i) : null;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanAddCatchAll));
        AddCatchAllCommand.NotifyCanExecuteChanged();
    }

    // The earlier rule that provably takes everything this one would, or null. Deliberately
    // partial: it reports only what it can prove, so a rule it stays quiet about may still
    // overlap — the point is to catch a rule that can never match at all.
    private string? ShadowedBy(int index)
    {
        for (var earlier = 0; earlier < index; earlier++)
        {
            if (RoutingRuleAnalysis.Subsumes(Rows[earlier].Destination, Rows[index].Destination))
            {
                return Localizer.Format(
                    Strings.Workspace_ShadowedByFormat, earlier + 1, Rows[earlier].Name);
            }
        }

        return null;
    }
}
