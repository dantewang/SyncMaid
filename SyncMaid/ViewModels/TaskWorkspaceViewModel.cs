using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
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
    private const int SampleSize = 8;
    private const int ExtensionChipCount = 12;

    private readonly SyncTask _task;
    private readonly IFolderPickerService _folderPicker;
    private readonly Func<string, string?> _crossTaskConflicts;
    private readonly IFileSystem? _fileSystem;

    public TaskWorkspaceViewModel(
        SyncTask task,
        IFolderPickerService folderPicker,
        Func<string, string?>? crossTaskConflicts = null,
        Guid? expand = null,
        bool startWithNewRule = false,
        IFileSystem? fileSystem = null)
    {
        _task = task;
        _folderPicker = folderPicker;
        _crossTaskConflicts = crossTaskConflicts ?? (_ => null);
        _fileSystem = fileSystem;

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

    /// <summary>True while a preview scan is running; the button says so and stays disabled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    private bool _isScanning;

    /// <summary>The preview's headline: how many files the source holds, or why it could not
    /// be read. Null before the first scan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string? _previewSummary;

    /// <summary>What no rule claims, and so stays in the source. Null when nothing does.</summary>
    [ObservableProperty]
    private string? _previewUnmatched;

    /// <summary>True once a scan has run, so the preview panel has something to show.</summary>
    public bool HasPreview => PreviewSummary is not null;

    /// <summary>
    /// Files more than one rule matched, with the rule that actually wins them. Information,
    /// not a problem: the ordering resolved it, and seeing which rule won is the point.
    /// </summary>
    public ObservableCollection<string> Contested { get; } = [];

    /// <summary>The file types the last scan found in the source, offered inside an open
    /// editor as one-click rules. Empty until a scan has run.</summary>
    public ObservableCollection<ExtensionChip> SourceExtensions { get; } = [];

    /// <summary>Whether a preview can be run at all (it needs a filesystem to read).</summary>
    public bool CanPreview => _fileSystem is not null;

    /// <summary>
    /// Scans the source and shows where each file would go — the same first-match-wins
    /// assignment the engine runs, so what this shows is what a run would do. Reads only:
    /// nothing is written and no plan is applied.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async System.Threading.Tasks.Task Rescan()
    {
        if (_fileSystem is not { } fileSystem)
        {
            return;
        }

        // Only rules that exist: a row still being written has no destination behind it.
        var destinations = Rows.Where(row => !row.IsDraft).Select(row => row.Destination).ToList();
        IsScanning = true;
        try
        {
            var scan = await System.Threading.Tasks.Task.Run(
                () => Scan(fileSystem, _task, destinations));
            ShowPreview(scan);
        }
        catch (Exception exception)
        {
            // A source that cannot be read is worth saying out loud — an empty preview would
            // otherwise read as "no files match your rules".
            ClearPreview();
            PreviewSummary = Localizer.Format(Strings.Workspace_PreviewFailedFormat, exception.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanRescan() => CanPreview && !IsScanning;

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

    /// <summary>
    /// Why the last save was refused, or null. A refusal only ever comes from a rule that is
    /// not finished — the workspace itself has nothing to validate.
    /// </summary>
    [ObservableProperty]
    private string? _saveBlockedMessage;

    /// <summary>
    /// Saving means "keep what I typed", so a rule still being edited is committed rather than
    /// dropped — forgetting to close the editor first used to lose the edit silently, and a
    /// row that was already saved would quietly revert to its previous state.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (!TryCommitOpenEditors())
        {
            return;
        }

        // Anything still a draft is a rule the user abandoned rather than one they were
        // editing: the commit above accepted every editor that could be accepted.
        Close(Rows.Where(row => !row.IsDraft).Select(row => row.Destination).ToList());
    }

    // Accepts every open editor, stopping at the first one that cannot be accepted — that rule
    // stays open with the reason shown, so what needs finishing is on screen rather than
    // discarded. Iterates a snapshot: accepting an editor can reorder or remove its row.
    private bool TryCommitOpenEditors()
    {
        foreach (var row in Rows.ToList())
        {
            if (row.Editor is not { } editor)
            {
                continue;
            }

            if (editor.IncompleteReason is { } reason)
            {
                SaveBlockedMessage = Localizer.Format(Strings.Workspace_SaveBlockedFormat, reason);
                return false;
            }

            editor.OKCommand.Execute(null);
        }

        SaveBlockedMessage = null;
        return true;
    }

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
        // Whatever the refusal was about, closing an editor is the user acting on it.
        SaveBlockedMessage = null;

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

    // The scan itself: one walk of the source, then exactly the assignment the engine would
    // make from it. Runs off the UI thread and touches nothing but the listing.
    private static ScanResult Scan(
        IFileSystem fileSystem, SyncTask task, IReadOnlyList<Destination> destinations)
    {
        var files = fileSystem.ListTree(task.SourcePath).Files;
        var perDestination = new Dictionary<Guid, (int Count, List<string> Sample)>();
        foreach (var destination in destinations)
        {
            perDestination[destination.Id] = (0, []);
        }

        var unmatched = new List<string>();
        var unmatchedCount = 0;
        var contested = new List<(string Path, IReadOnlyList<int> Rules)>();

        if (task.Kind == SyncTaskKind.Move)
        {
            var routing = MoveRouting.Route(destinations, files);
            for (var i = 0; i < destinations.Count; i++)
            {
                var assigned = routing.For(i);
                perDestination[destinations[i].Id] =
                    (assigned.Count, assigned.Take(SampleSize).Select(file => file.RelativePath).ToList());
            }

            unmatchedCount = routing.Unmatched.Count;
            unmatched.AddRange(routing.Unmatched.Take(SampleSize).Select(file => file.RelativePath));
            contested.AddRange(routing.Contested
                .Take(SampleSize)
                .Select(entry => (entry.Key, entry.Value)));
        }
        else
        {
            // Copying destinations are independent, so a file can be in several of them and
            // "unmatched" means no destination wanted it at all.
            foreach (var file in files)
            {
                var claimed = false;
                foreach (var destination in destinations)
                {
                    if (!destination.Includes(file.RelativePath))
                    {
                        continue;
                    }

                    claimed = true;
                    var (count, sample) = perDestination[destination.Id];
                    if (sample.Count < SampleSize)
                    {
                        sample.Add(file.RelativePath);
                    }

                    perDestination[destination.Id] = (count + 1, sample);
                }

                if (!claimed)
                {
                    unmatchedCount++;
                    if (unmatched.Count < SampleSize)
                    {
                        unmatched.Add(file.RelativePath);
                    }
                }
            }
        }

        return new ScanResult(
            files.Count, perDestination, unmatchedCount, unmatched, contested, Extensions(files));
    }

    // The file types the source actually holds, commonest first. Authoring a rule is then
    // picking from what is there rather than guessing at globs.
    private static IReadOnlyList<(string Extension, int Count)> Extensions(
        IReadOnlyList<ListedFile> files)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var extension = System.IO.Path.GetExtension(file.RelativePath).TrimStart('.');
            if (extension.Length > 0)
            {
                counts[extension] = counts.GetValueOrDefault(extension) + 1;
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(ExtensionChipCount)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
    }

    private void ShowPreview(ScanResult scan)
    {
        PreviewSummary = Localizer.Format(
            Strings.Workspace_PreviewSourceFormat,
            Localizer.Plural("Common.FilesCount", scan.FileCount));

        foreach (var row in Rows)
        {
            if (scan.PerDestination.TryGetValue(row.Destination.Id, out var entry))
            {
                row.PreviewCount = Localizer.Plural("Common.FilesCount", entry.Count);
                row.PreviewSample = entry.Sample.Count == 0 ? null : string.Join("\n", entry.Sample);
            }
        }

        PreviewUnmatched = scan.UnmatchedCount == 0
            ? null
            : Localizer.Format(
                Strings.Workspace_PreviewUnmatchedFormat,
                Localizer.Plural("Common.FilesCount", scan.UnmatchedCount),
                string.Join(", ", scan.UnmatchedSample));

        SourceExtensions.Clear();
        foreach (var (extension, count) in scan.Extensions)
        {
            SourceExtensions.Add(new ExtensionChip(extension, $"{extension} ({count})"));
        }

        Contested.Clear();
        foreach (var (path, rules) in scan.Contested)
        {
            Contested.Add(Localizer.Format(
                Strings.Workspace_PreviewContestedFormat,
                path,
                string.Join(", ", rules.Select(rule => rule + 1)),
                Rows.ElementAtOrDefault(rules[0])?.Name ?? string.Empty));
        }
    }

    // A preview describes one set of rules; the moment they change it is a claim about
    // something that no longer exists, so it goes rather than quietly going stale.
    private void ClearPreview()
    {
        PreviewSummary = null;
        PreviewUnmatched = null;
        Contested.Clear();
        foreach (var row in Rows)
        {
            row.PreviewCount = null;
            row.PreviewSample = null;
        }
    }

    private sealed record ScanResult(
        int FileCount,
        IReadOnlyDictionary<Guid, (int Count, List<string> Sample)> PerDestination,
        int UnmatchedCount,
        IReadOnlyList<string> UnmatchedSample,
        IReadOnlyList<(string Path, IReadOnlyList<int> Rules)> Contested,
        IReadOnlyList<(string Extension, int Count)> Extensions);

    /// <summary>One file type present in the source, offered as a one-click rule.</summary>
    /// <param name="Extension">The bare extension, e.g. <c>pdf</c>.</param>
    /// <param name="Label">What the chip reads, e.g. <c>pdf (12)</c>.</param>
    public sealed record ExtensionChip(string Extension, string Label);

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
        ClearPreview();
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
