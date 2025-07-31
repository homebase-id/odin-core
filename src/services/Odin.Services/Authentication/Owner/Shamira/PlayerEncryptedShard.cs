namespace Odin.Services.Authentication.Owner.Shamira;

/// <summary>
/// Represents what the player stores: the shard encrypted by the dealer with the random key.
/// </summary>
public record PlayerEncryptedShard(ShamiraPlayer Player, byte[] DealerEncryptedShard);