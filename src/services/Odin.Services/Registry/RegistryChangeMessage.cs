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
}
