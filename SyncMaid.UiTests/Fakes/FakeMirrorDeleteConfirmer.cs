using System.Threading.Tasks;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>An <see cref="IMirrorDeleteConfirmer"/> that returns a preset answer and records calls.</summary>
public sealed class FakeMirrorDeleteConfirmer : IMirrorDeleteConfirmer
{
    public bool Approve { get; set; }
    public int CallCount { get; private set; }
    public MirrorDeleteRequest? LastRequest { get; private set; }

    public Task<bool> ConfirmAsync(MirrorDeleteRequest request)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(Approve);
    }
}
