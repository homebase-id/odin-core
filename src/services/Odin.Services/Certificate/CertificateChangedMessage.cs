using System;

namespace Odin.Services.Certificate;

#nullable enable

/// <summary>
/// Announced over <see cref="Odin.Core.Storage.PubSub.ISystemPubSub"/> when a node writes a
/// certificate, so the other nodes re-read the row. Carries the domain name only - never the
/// certificate, never the key.
/// </summary>
public sealed class CertificateChangedMessage
{
    public const string Channel = "certificate-changed";

    public string Domain { get; init; } = "";

    /// <summary>
    /// Lets the publisher ignore its own announcement, as
    /// <see cref="Odin.Services.Registry.RegistryChangeMessage.OriginNodeId"/> does.
    /// </summary>
    public Guid OriginNodeId { get; init; }
}
