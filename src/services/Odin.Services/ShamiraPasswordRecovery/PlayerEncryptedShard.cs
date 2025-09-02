using System;
using Odin.Core;
using Odin.Core.Serialization;
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

    public static string Serialize(PlayerEncryptedShard shard)
    {
        var json = OdinSystemSerializer.Serialize(shard);
        return json.ToUtf8ByteArray().ToBase64();
    }

    public static PlayerEncryptedShard Deserialize(string content)
    {
        return OdinSystemSerializer.Deserialize<PlayerEncryptedShard>(content.FromBase64().ToStringFromUtf8Bytes());
    }
}