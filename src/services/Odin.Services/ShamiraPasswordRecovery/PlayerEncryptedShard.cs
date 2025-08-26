using System;
using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery;

/// <summary>
/// Represents what the player stores: the shard encrypted by the dealer with the random key.
/// </summary>
public class PlayerEncryptedShard
{
    public Guid Id { get; init; }
    public int Index { get; init; }
    public ShamiraPlayer Player { get; init; }
    public UnixTimeUtc Created { get; init; }
    public byte[] DealerEncryptedShard { get; init; }
}