using SyncMaid.Core.IO;

namespace SyncMaid.Core.Tests.IO;

public class FileBusyTests
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int DiskFull = unchecked((int)0x80070070);

    [Theory]
    [InlineData(SharingViolation)]
    [InlineData(LockViolation)]
    public void Recognizes_a_file_held_open_by_another_process(int hresult)
    {
        Assert.True(FileBusy.IsBusy(new IOException("in use", hresult)));
    }

    // The engine wraps operation failures, so the signal has to survive nesting.
    [Fact]
    public void Recognizes_a_violation_carried_as_an_inner_exception()
    {
        var inner = new IOException("in use", SharingViolation);

        Assert.True(FileBusy.IsBusy(new InvalidOperationException("copy failed", inner)));
    }

    [Fact]
    public void Does_not_treat_other_failures_as_busy()
    {
        Assert.False(FileBusy.IsBusy(new IOException("disk full", DiskFull)));
        Assert.False(FileBusy.IsBusy(new IOException("generic")));
        Assert.False(FileBusy.IsBusy(new UnauthorizedAccessException("permission denied")));
        Assert.False(FileBusy.IsBusy(new FileNotFoundException("gone")));
        Assert.False(FileBusy.IsBusy(null));
    }
}
