namespace SyncMaid.Services;

/// <summary>
/// Broadcast (via <c>WeakReferenceMessenger</c>) by <see cref="Localizer.Apply"/> after the
/// UI culture changes, so view models re-evaluate their computed display strings.
/// </summary>
public sealed record CultureChangedMessage;
