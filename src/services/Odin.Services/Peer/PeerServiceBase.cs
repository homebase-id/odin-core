using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Services.Util;

namespace Odin.Services.Peer
{
    /// <summary>
    /// Base class for the transit subsystem providing various functions specific to Transit
    /// </summary>
    public abstract class PeerServiceBase(
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver)
    {
        protected readonly IOdinHttpClientFactory OdinHttpClientFactory = odinHttpClientFactory;


        protected readonly CircleNetworkService CircleNetworkService = circleNetworkService;

        protected FileSystemResolver FileSystemResolver { get; } = fileSystemResolver;
        
        protected SharedSecretEncryptedTransitPayload CreateSharedSecretEncryptedPayload(ClientAccessToken token, object o)
        {
            var iv = ByteArrayUtil.GetRndByteArray(16);
            // var key = token?.SharedSecret ?? new SensitiveByteArray(Guid.Empty.ToByteArray());
            var jsonBytes = OdinSystemSerializer.Serialize(o).ToUtf8ByteArray();
            // var encryptedBytes = AesCbc.Encrypt(jsonBytes, ref key, iv);
            var encryptedBytes = jsonBytes;

            var payload = new SharedSecretEncryptedTransitPayload()
            {
                Iv = iv,
                Data = Convert.ToBase64String(encryptedBytes)
            };

            return payload;
        }

        protected async Task<ClientAccessToken> ResolveClientAccessTokenAsync(OdinId recipient, IOdinContext odinContext,
            bool failIfNotConnected = true)
        {
            //TODO: this check is duplicated in the TransitQueryService.CreateClient method; need to centralize
            odinContext.PermissionsContext.AssertHasAtLeastOnePermission(
                PermissionKeys.UseTransitWrite,
                PermissionKeys.UseTransitRead);

            //Note here we overrideHack the permission check because we have either UseTransitWrite or UseTransitRead
            var icr = await CircleNetworkService.GetIcrAsync(recipient, odinContext, overrideHack: true);
            if (icr?.IsConnected() == false)
            {
                if (failIfNotConnected)
                {
                    throw new OdinClientException("Cannot resolve client access token; not connected",
                        OdinClientErrorCode.NotAConnectedIdentity);
                }

                return null;
            }


            return icr!.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey());
        }

        protected async Task<(ClientAccessToken token, IPeerReactionHttpClient client)> CreateReactionContentClientAsync(OdinId odinId, IOdinContext odinContext,
            FileSystemType? fileSystemType = null)
        {
            var token = await ResolveClientAccessTokenAsync(odinId, odinContext, false);

            if (token == null)
            {
                var httpClient = OdinHttpClientFactory.CreateClient<IPeerReactionHttpClient>(odinId, fileSystemType);
                return (null, httpClient);
            }
            else
            {
                var httpClient = OdinHttpClientFactory.CreateClientUsingAccessToken<IPeerReactionHttpClient>(
                    odinId,
                    token.ToAuthenticationToken(),
                    fileSystemType);
                return (token, httpClient);
            }
        }

        protected async Task<(ClientAccessToken token, T client)> CreateHttpClientAsync<T>(
            OdinId odinId,
            IdentityDatabase db,
            IOdinContext odinContext)
        {

            var token = await ResolveClientAccessTokenAsync(odinId, odinContext);

            var httpClient = OdinHttpClientFactory.CreateClientUsingAccessToken<T>(
                odinId,
                token.ToAuthenticationToken());
        
            return (token, httpClient);
        }
        
        protected async Task<T> DecryptUsingSharedSecretAsync<T>(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
        {
            var caller = odinContext.Caller.OdinId;
            OdinValidationUtils.AssertIsTrue(caller.HasValue, "Caller OdinId missing");

            //TODO: put decryption back in place
            // var t = await ResolveClientAccessToken(caller!.Value, tokenSource);
            // var sharedSecret = t.SharedSecret;
            // var encryptedBytes = Convert.FromBase64String(payload.Data);
            // var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref sharedSecret, payload.Iv);

            var decryptedBytes = Convert.FromBase64String(payload.Data);
            var json = decryptedBytes.ToStringFromUtf8Bytes();
            await Task.CompletedTask;
            return OdinSystemSerializer.Deserialize<T>(json);
        }

        /// <summary>
        /// Looks up a file by a global transit identifier
        /// </summary>
        protected async Task<InternalDriveFileId?> ResolveInternalFile(GlobalTransitIdFileIdentifier file, IOdinContext odinContext,
            IdentityDatabase db,
            bool failIfNull = false)
        {
            var (_, fileId) = await FileSystemResolver.ResolveFileSystem(file, odinContext, db);

            if (failIfNull && fileId == null)
            {
                // throw new OdinRemoteIdentityException($"Invalid global transit id {file.GlobalTransitId} on drive {file.TargetDrive}");
                // logger.LogInformation($"Invalid global transit id {file.GlobalTransitId} on drive {file.TargetDrive}");
                throw new OdinClientException($"Invalid global transit id {file.GlobalTransitId} on drive {file.TargetDrive}",
                    OdinClientErrorCode.InvalidGlobalTransitId);
            }

            return fileId;
        }
    }
}