using System.Text.Json.Serialization;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// for persisting tasks. Source generation (rather than reflection-based
/// serialization) is what keeps persistence AOT/trim-safe — the serializer for every
/// type below is emitted at compile time.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<SyncTask>))]
[JsonSerializable(typeof(List<DestinationSyncStatus>))]
internal sealed partial class TaskStoreJsonContext : JsonSerializerContext;
