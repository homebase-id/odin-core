using System;

namespace Odin.Services.Registry;

#nullable enable

public enum RegistryChangeKind
{
    Upserted,
    Deleted,
}

/// <summary>
/// Announces that a registration changed, so other nodes can refresh their in-memory registry.
/// </summary>
/// <remarks>
/// The message deliberately carries an identifier and nothing else: the receiver always re-reads
/// the row from the database. That makes duplicate and out-of-order delivery harmless, and means a
/// late message can never overwrite newer state. Delivery is at-most-once, so this is only an
/// accelerator; <see cref="FileSystemIdentityRegistry.ReconcileWithDatabaseAsync"/> is what
/// guarantees convergence.
/// </remarks>
public sealed class RegistryChangeMessage
{
    public const string Channel = "registry-changed";

    public Guid IdentityId { get; init; }
    public string PrimaryDomain { get; init; } = "";
    public RegistryChangeKind Kind { get; init; }

    /// <summary>Set so the publishing node can ignore its own message; it already applied the change.</summary>
    public Guid OriginNodeId { get; init; }
}
