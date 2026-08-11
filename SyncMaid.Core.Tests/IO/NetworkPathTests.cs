using SyncMaid.Core.IO;

namespace SyncMaid.Core.Tests.IO;

/// <summary>
/// <see cref="NetworkPath.IsNetwork"/> gates two behaviours that differ on a share: the
/// recycle-to-hard-delete fallback (a share has no Recycle Bin) and the choice of a
/// polling watcher over the unreliable OS one. A wrong answer is silent in both
/// directions — deletions become unrecoverable, or a task quietly stops noticing changes.
/// </summary>
public class NetworkPathTests
{
    [Theory]
    [InlineData(@"\\nas\share")]
    [InlineData(@"\\nas\share\photos\2026")]
    [InlineData(@"\\127.0.0.1\c$")]
    public void UNC_paths_are_network(string path) =>
        Assert.True(NetworkPath.IsNetwork(path));

    [Theory]
    [InlineData(@"C:\Users\dante\Documents")]
    [InlineData(@"C:\")]
    public void Local_fixed_paths_are_not_network(string path) =>
        Assert.False(NetworkPath.IsNetwork(path));

    // Callers pass user-typed and half-typed paths (the editors probe on every keystroke),
    // so unresolvable input must answer "not network" rather than throw.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\")]
    [InlineData(@"\\")]
    [InlineData(@"\\partially-typed")]
    [InlineData("not a path at all")]
    [InlineData("Z:\\a\\path\\on\\a\\drive\\that\\does\\not\\exist")]
    public void Unresolvable_input_does_not_throw(string path)
    {
        var exception = Record.Exception(() => NetworkPath.IsNetwork(path));

        Assert.Null(exception);
    }

    // A partial UNC prefix still counts as network: it can only ever resolve to one, and
    // treating it as local would pick the wrong watcher for the eventual real path.
    [Fact]
    public void A_bare_UNC_prefix_is_treated_as_network() =>
        Assert.True(NetworkPath.IsNetwork(@"\\"));
}
