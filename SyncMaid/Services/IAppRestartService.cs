namespace SyncMaid.Services;

/// <summary>Relaunches SyncMaid — a fresh instance starts and the current one exits. Used after
/// a config-location switch, whose new paths only take effect at startup.</summary>
public interface IAppRestartService
{
    void Restart();
}
