using System;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Storage;

namespace Odin.Core.Services.Peer.SendingHost
{
    /// <summary>
    /// Base class for the transit subsystem providing various functions specific to Transit
    /// </summary>
    public abstract class TransitServiceBase
    {
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly FollowerService _followerService;
        private readonly FileSystemResolver _fileSystemResolver;


        protected OdinContext OdinContext => _contextAccessor.GetCurrent();

        protected TransitServiceBase(IOdinHttpClientFactory odinHttpClientFactory, CircleNetworkService circleNetworkService,
            OdinContextAccessor contextAccessor, FollowerService followerService, FileSystemResolver fileSystemResolver)
        {
            _odinHttpClientFactory = odinHttpClientFactory;
            _circleNetworkService = circleNetworkService;
            _contextAccessor = contextAccessor;
            _followerService = followerService;
            _fileSystemResolver = fileSystemResolver;
        }

        protected SharedSecretEncryptedTransitPayload CreateSharedSecretEncryptedPayload(ClientAccessToken token, object o)
        {
            var iv = ByteArrayUtil.GetRndByteArray(16);
            var key = token?.SharedSecret ?? new SensitiveByteArray(Guid.Empty.ToByteArray());
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
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasAtLeastOnePermission(
                PermissionKeys.UseTransitWrite,
                PermissionKeys.UseTransitRead);
            
            //Note here we overrideHack the permission check because we have either UseTransitWrite or UseTransitRead
            var icr = await _circleNetworkService.GetIdentityConnectionRegistration(recipient, overrideHack: true);
            if (icr?.IsConnected() == false)
            {
                if (failIfNotConnected)
                {
                    return null;
                }

                throw new OdinClientException("Cannot resolve client access token; not connected", OdinClientErrorCode.NotAConnectedIdentity);
            }

            return icr!.CreateClientAccessToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey());

        }

        protected async Task<(ClientAccessToken token, ITransitHostReactionHttpClient client)> CreateReactionContentClient(OdinId odinId, bool failIfNotConnected = true,
            FileSystemType? fileSystemType = null)
        {
            var token = await ResolveClientAccessToken(odinId, failIfNotConnected);

            if (token == null)
            {
                var httpClient = _odinHttpClientFactory.CreateClient<ITransitHostReactionHttpClient>(odinId, fileSystemType);
                return (null, httpClient);
            }
            else
            {
                var httpClient = _odinHttpClientFactory.CreateClientUsingAccessToken<ITransitHostReactionHttpClient>(odinId, token.ToAuthenticationToken(), fileSystemType);
                return (token, httpClient);
            }
        }

        protected async Task<T> DecryptUsingSharedSecret<T>(SharedSecretEncryptedTransitPayload payload)
        {
            var caller = OdinContext.Caller.OdinId;
            Guard.Argument(caller, nameof(OdinContext.Caller.OdinId)).NotNull().Require(v => v.HasValue());

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
        protected async Task<InternalDriveFileId?> ResolveInternalFile(GlobalTransitIdFileIdentifier file)
        {
            var (_, fileId) = await _fileSystemResolver.ResolveFileSystem(file);
            return fileId;
        }
    }
}