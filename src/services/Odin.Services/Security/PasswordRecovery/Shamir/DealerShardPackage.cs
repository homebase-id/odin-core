using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// Contains shard information retained by the dealer
/// </summary>
public class DealerShardPackage
{
    public int MinMatchingShards { get; init; }
    public List<DealerShardEnvelope> Envelopes { get; init; }
    public UnixTimeUtc Updated { get; set; }

    public static string Serialize(DealerShardPackage package)
    {
        return OdinSystemSerializer.Serialize(package).ToUtf8ByteArray().ToBase64();
    }

    public static DealerShardPackage Deserialize(string json)
    {
        return OdinSystemSerializer.Deserialize<DealerShardPackage>(json.FromBase64().ToStringFromUtf8Bytes());
    }
}