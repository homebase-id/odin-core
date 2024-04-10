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
        IOdinContextAccessor contextAccessor,
        FileSystemResolver fileSystemResolver)
    {

        protected OdinContext OdinContext => contextAccessor.GetCurrent();

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

        protected async Task<ClientAccessToken> ResolveClientAccessToken(OdinId recipient, bool failIfNotConnected = true)
        {
            //TODO: this check is duplicated in the TransitQueryService.CreateClient method; need to centralize
            contextAccessor.GetCurrent().PermissionsContext.AssertHasAtLeastOnePermission(
                PermissionKeys.UseTransitWrite,
                PermissionKeys.UseTransitRead);
            
            //Note here we overrideHack the permission check because we have either UseTransitWrite or UseTransitRead
            var icr = await circleNetworkService.GetIdentityConnectionRegistration(recipient, overrideHack: true);
            if (icr?.IsConnected() == false)
            {
                if (failIfNotConnected)
                {
                    throw new OdinClientException("Cannot resolve client access token; not connected", OdinClientErrorCode.NotAConnectedIdentity);
                }
                
                return null;
            }

            return icr!.CreateClientAccessToken(contextAccessor.GetCurrent().PermissionsContext.GetIcrKey());

        }

        protected async Task<(ClientAccessToken token, IPeerReactionHttpClient client)> CreateReactionContentClient(OdinId odinId, FileSystemType? fileSystemType = null)
        {
            var token = await ResolveClientAccessToken(odinId, false);

            if (token == null)
            {
                var httpClient = odinHttpClientFactory.CreateClient<IPeerReactionHttpClient>(odinId, fileSystemType);
                return (null, httpClient);
            }
            else
            {
                var httpClient = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerReactionHttpClient>(odinId, token.ToAuthenticationToken(), fileSystemType);
                return (token, httpClient);
            }
        }

        protected async Task<T> DecryptUsingSharedSecret<T>(SharedSecretEncryptedTransitPayload payload)
        {
            var caller = OdinContext.Caller.OdinId;
            OdinValidationUtils.AssertIsTrue(caller.HasValue, "Caller OdinId missing");

            //TODO: put decryption back in place
            // var t = await ResolveClientAccessToken(caller!.Value, tokenSource);
            // var sharedSecret = t.SharedSecret;
            // var encryptedBytes = Convert.FromBase64String(payload.Data);
            // var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref sharedSecret, payload.Iv);

            var decryptedBytes = Convert.FromBase64String(payload.Data);
            var json = decryptedBytes.ToStringFromUtf8Bytes();
            return await Task.FromResult(OdinSystemSerializer.Deserialize<T>(json));
        }

        /// <summary>
        /// Looks up a file by a global transit identifier
        /// </summary>
        protected async Task<InternalDriveFileId?> ResolveInternalFileByGlobalTransitId(GlobalTransitIdFileIdentifier file)
        {
            var (_, fileId) = await fileSystemResolver.ResolveFileSystem(file);
            return fileId;
        }
    }
}