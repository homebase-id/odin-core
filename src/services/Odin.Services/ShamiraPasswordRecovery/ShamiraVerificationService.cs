using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Membership.Connections;

namespace Odin.Services.ShamiraPasswordRecovery;

public class ShamiraVerificationService(
    ShamiraRecoveryService recoveryService,
    CircleNetworkService circleNetworkService,
    OdinHttpClientFactory odinHttpClientFactory,
    StandardFileSystem fileSystem)
{
    /// <summary>
    /// Verifies shards held by players
    /// </summary>
    public async Task<Dictionary<OdinId, bool>> VerifyRemotePlayerShards(IOdinContext odinContext)
    {
        // get the preconfigured package
        var package = await recoveryService.GetDealerEnvelop(odinContext);

        if (package == null)
        {
            return new Dictionary<OdinId, bool>();
        }

        var results = new Dictionary<OdinId, bool>();
        foreach (var envelope in package.Envelopes)
        {
            var (_, client) = await CreateClientAsync(envelope.Player.OdinId, FileSystemType.Standard, odinContext);
            var response = await client.VerifyShard(new VerifyShardRequest()
            {
                ShardId = envelope.ShardId
            });

            //TODO: expand on this
            var isValid = response.IsSuccessStatusCode && response.Content.IsValid;
            results.Add(envelope.Player.OdinId, isValid);
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