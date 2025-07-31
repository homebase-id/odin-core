using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Obsolete;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;

namespace Odin.Services.Authentication.Owner.Shamira;

public class ShamiraRecoveryService(
    TableKeyValue tblKeyValue,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService)
{
    private static readonly Guid RecordStorageId = Guid.Parse("2fd6e22e-77a2-4f9b-8024-0d037ffbaba1");
    private const string ContextKey = "437328ea-2485-4e75-b26d-8f2254564fd2";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    /// <summary>
    /// Creates encrypted shards for the specified players
    /// </summary>
    public Task<(
            List<DealerShardEnvelope> DealerRecords,
            List<PlayerEncryptedShard> PlayerRecords)>
        CreateShards(
            List<ShamiraPlayer> players,
            int totalShards,
            int minShards,
            SensitiveByteArray secret,
            IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var shards = ShamirSecretSharing.GenerateShamirShares(totalShards, minShards, secret.GetKey());

        OdinValidationUtils.AssertIsTrue(players.TrueForAll(p => p.Type == PlayerType.Delegate), "Only Delegate player type is supported");
        OdinValidationUtils.AssertIsTrue(shards.Count == players.Count, "Player and shard count do not match");

        var dealerRecords = new List<DealerShardEnvelope>();
        var playerRecords = new List<PlayerEncryptedShard>();

        // give each player a shard; encrypted with a key
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var playerEncryptionKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            // Dealer encrypts a player's shard with the random key
            var (iv, cipher) = AesCbc.Encrypt(shards[i].Shard, playerEncryptionKey);

            playerRecords.Add(new PlayerEncryptedShard(player, cipher));
            dealerRecords.Add(new DealerShardEnvelope()
            {
                Player = player,
                EncryptionKey = playerEncryptionKey.GetKey(),
                EncryptionIv = iv
            });
        }

        return Task.FromResult((dealerRecords, playerRecords));
    }

    public async Task SaveDealerEnvelop(ShardEnvelop envelope, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        //TODO: are we encrypting anything here?
        // var masterKey = odinContext.Caller.GetMasterKey();
        await Storage.UpsertAsync(tblKeyValue, RecordStorageId, envelope);
    }

    /// <summary>
    /// Sends the shards to the list identities
    /// </summary>
    /// <param name="shards"></param>
    /// <param name="odinContext"></param>
    public async Task<Dictionary<OdinId, bool>> DistributeShards(List<PlayerEncryptedShard> shards, IOdinContext odinContext)
    {
        var results = new Dictionary<OdinId, bool>();
        foreach (var shard in shards)
        {
            var (_, client) = await CreateClientAsync(shard.Player.OdinId, FileSystemType.Standard, odinContext);
            var response = await client.SendShard(new SendShardRequest()
            {
                DealerEncryptedShard = shard.DealerEncryptedShard
            });
            
            results.Add(shard.Player.OdinId, response.IsSuccessStatusCode);
        }

        return results;
    }

    private async Task<ShardEnvelop> GetKeyInternalAsync()
    {
        var existingKey = await Storage.GetAsync<ShardEnvelop>(tblKeyValue, RecordStorageId);
        return existingKey;
    }

    private async Task<(IdentityConnectionRegistration, IPeerPasswordRecoveryHttpClient)> CreateClientAsync(OdinId odinId,
        FileSystemType? fileSystemType,
        IOdinContext odinContext)
    {
        var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);
        var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        var httpClient = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerPasswordRecoveryHttpClient>(
            odinId, authToken, fileSystemType);
        return (icr, httpClient);
    }
}