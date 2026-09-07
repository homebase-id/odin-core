using System;

namespace Odin.Services.Registry;

#nullable enable

/// <summary>
/// Announces that the registry version moved, so other nodes can decide whether they are behind.
/// </summary>
/// <remarks>
/// The message carries the version and nothing else: the receiver compares it to the version it
/// last read from the database and reconciles from the database if it is behind. Delivery is
/// at-most-once, so this is the fast path only; the version row in Postgres, re-read on startup
/// and whenever the Redis connection is restored, is what guarantees convergence.
/// </remarks>
public sealed class RegistryChangeMessage
{
    public const string Channel = "registry-changed";

    public long Version { get; init; }

    /// <summary>
    /// The in-process pub/sub broker delivers a message back to its own publisher, and the
    /// publishing node has already applied the change, so it must ignore its own announcement or
    /// it would reconcile a tenant it is still in the middle of bringing up.
    /// </summary>
    public Guid OriginNodeId { get; init; }
}
