namespace SyncMaid.ViewModels;

/// <summary>
/// A destination path the user is typing already belongs to something else. Destinations
/// never overlap — two of them writing one tree race on the same files — and the two ways
/// that happens need different words: a sibling in this very task, or a destination of
/// another task entirely.
/// </summary>
/// <param name="Name">The destination's name when <paramref name="WithinTask"/>, otherwise
/// the name of the task owning it.</param>
/// <param name="WithinTask">True when the clash is with another destination of the same task.</param>
public sealed record DestinationConflict(string Name, bool WithinTask);
