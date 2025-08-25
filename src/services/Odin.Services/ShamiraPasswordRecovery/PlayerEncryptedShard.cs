using System;

namespace Odin.Services.ShamiraPasswordRecovery;

/// <summary>
/// Represents what the player stores: the shard encrypted by the dealer with the random key.
/// </summary>
public record PlayerEncryptedShard(Guid Id, ShamiraPlayer Player, byte[] DealerEncryptedShard)
{
}