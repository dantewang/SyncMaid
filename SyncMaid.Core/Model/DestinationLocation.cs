using System.Text.Json.Serialization;

namespace SyncMaid.Core.Model;

/// <summary>
/// Where a destination's files live. A closed, JSON-discriminated hierarchy — the same
/// pattern as <see cref="Filtering.FilterRule"/> and <see cref="Triggers.Trigger"/> — so
/// the serializer needs no reflection and the engine stays AOT/trim-safe. Today the only
/// kind is <see cref="LocalDestination"/> (a local or mounted path); cloud and SFTP
/// locations slot in as new derived types plus a matching provider, without touching the
/// engine (see docs/location-and-verification-design.md).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LocalDestination), "local")]
public abstract record DestinationLocation;

/// <summary>A local directory or a pre-mounted network path (mapped drive / UNC).</summary>
public sealed record LocalDestination(string Path) : DestinationLocation;
