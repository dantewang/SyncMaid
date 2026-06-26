namespace SyncMaid.ViewModels;

/// <summary>
/// The filter choices the destination editor offers when not syncing all files. ("All
/// files" is its own toggle, mapping to <see cref="Core.Filtering.AllFilesFilter"/>.)
/// The editor maps this to the concrete <see cref="Core.Filtering.FilterRule"/> on add.
/// </summary>
public enum FilterKind
{
    Path,
    Extension,
}
