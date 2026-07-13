using SyncMaid.Lang;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.Views;

/// <summary>
/// Stand-in for the app's many computed display-string properties: get-only, no change
/// notification of its own — it re-evaluates only through <see cref="ViewModelBase"/>'s
/// culture-changed refresh.
/// </summary>
public sealed class LocProbeViewModel : ViewModelBase
{
    public string HealthProbe => Strings.Task_HealthAllSynced;
}
