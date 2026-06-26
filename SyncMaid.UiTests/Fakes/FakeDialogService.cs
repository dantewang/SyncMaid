using System;
using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>
/// Stands in for the editor dialogs. Tests set <see cref="OnEditTask"/> /
/// <see cref="OnEditDestination"/> to decide what the "dialog" returns.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    public Func<SyncTask?, SyncTask?> OnEditTask { get; set; } = _ => null;
    public Func<Destination?, Destination?> OnEditDestination { get; set; } = _ => null;

    public Task<SyncTask?> EditTaskAsync(SyncTask? existing) => Task.FromResult(OnEditTask(existing));

    public Task<Destination?> EditDestinationAsync(Destination? existing) =>
        Task.FromResult(OnEditDestination(existing));
}
