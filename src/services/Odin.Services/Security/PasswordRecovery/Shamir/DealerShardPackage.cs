using System;
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
    /// <summary>
    /// The minimum number of shards required to recover the key
    /// </summary>
    public int MinMatchingShards { get; init; }
    
    /// <summary>
    /// The Dealers list of shards; describing the players and the encryption key to decrypt the <see cref="PlayerEncryptedShard"/>
    /// </summary>
    public List<DealerShardEnvelope> Envelopes { get; init; }
    
    public UnixTimeUtc Updated { get; set; }
    
    public bool UsesAutomatedRecovery { get; set; }

    /// <summary>
    /// The recovery email's hash the deal was dealt with, as each shard stores it.  Another than today's
    /// means the email changed since: the browser deals again.  Null for a deal from before deals
    /// recorded it.
    /// </summary>
    public Guid? RecoveryEmailHash { get; set; }

    public static string Serialize(DealerShardPackage package)
    {
        return OdinSystemSerializer.Serialize(package).ToUtf8ByteArray().ToBase64();
    }

    public static DealerShardPackage Deserialize(string json)
    {
        return OdinSystemSerializer.Deserialize<DealerShardPackage>(json.FromBase64().ToStringFromUtf8Bytes());
    }
}