using System.Threading.Tasks;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>Returns a preset folder (or null) and records the title it was asked with.</summary>
public sealed class FakeFolderPickerService : IFolderPickerService
{
    private readonly string? _result;

    public FakeFolderPickerService(string? result = null) => _result = result;

    public string? LastTitle { get; private set; }

    public Task<string?> PickFolderAsync(string title)
    {
        LastTitle = title;
        return Task.FromResult(_result);
    }
}
