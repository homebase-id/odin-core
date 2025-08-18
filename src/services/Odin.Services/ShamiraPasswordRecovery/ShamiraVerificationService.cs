using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Membership.Connections;

namespace Odin.Services.ShamiraPasswordRecovery;

public class ShamiraVerificationService(
    CircleNetworkService circleNetworkService,
    OdinHttpClientFactory odinHttpClientFactory,
    StandardFileSystem fileSystem)
{
    private static readonly Guid RecordStorageId = Guid.Parse("2fd6e22e-77a2-4f9b-8024-0d037ffbaba1");
    private const string ContextKey = "437328ea-2485-4e75-b26d-8f2254564fd2";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    /// <summary>
    /// Verifies shards held by players
    /// </summary>
    public async Task<Dictionary<OdinId, bool>> VerifyRemotePlayerShards(List<PlayerEncryptedShard> shards, IOdinContext odinContext)
    {
        // call out to each shard holder ensuring they have the file

        var results = new Dictionary<OdinId, bool>();
        foreach (var shard in shards)
        {
            var (_, client) = await CreateClientAsync(shard.Player.OdinId, FileSystemType.Standard, odinContext);
            var response = await client.VerifyShard(new VerifyShardRequest()
            {
                ShardId = shard.Id
            });

            //TODO: expand on this
            var isValid = response.IsSuccessStatusCode && response.Content.IsValid;
            results.Add(shard.Player.OdinId, isValid);
        }

        return results;
    }

    public async Task<ShardVerificationResult> VerifyShard(Guid shardId, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var dealer = odinContext.Caller.OdinId;

        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false,
            IncludeTransferHistory = false
        };
        
        var file = await fileSystem.Query.GetFileByClientUniqueId(driveId, shardId, options, odinContext);

        if (null == file)
        {
            return new ShardVerificationResult()
            {
                IsValid = false
            };
        }

        //TODO: what else to verify?
        var isValid = file.FileMetadata.SenderOdinId == dealer;
        return new ShardVerificationResult()
        {
            IsValid = isValid
        };
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