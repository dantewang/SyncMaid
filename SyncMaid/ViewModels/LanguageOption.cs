namespace SyncMaid.ViewModels;

/// <summary>
/// One entry in the Settings language picker: the culture tag persisted to settings
/// (null = follow the OS language) and its display name. Concrete languages show their
/// own native name (standard picker practice — a user locked out by the current language
/// can still find theirs); only "System default" is a localized resource.
/// </summary>
public sealed record LanguageOption(string? Tag, string DisplayName);
