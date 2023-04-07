using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.SendingHost
{
    /// <summary>
    /// Base class for the transit subsystem providing various functions specific to Transit
    /// </summary>
    public abstract class TransitServiceBase
    {
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly FollowerService _followerService;
        private readonly FileSystemResolver _fileSystemResolver;


        protected DotYouContext DotYouContext => _contextAccessor.GetCurrent();

        protected TransitServiceBase(IDotYouHttpClientFactory dotYouHttpClientFactory, ICircleNetworkService circleNetworkService,
            DotYouContextAccessor contextAccessor, FollowerService followerService, FileSystemResolver fileSystemResolver)
        {
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _circleNetworkService = circleNetworkService;
            _contextAccessor = contextAccessor;
            _followerService = followerService;
            _fileSystemResolver = fileSystemResolver;
        }

        protected SharedSecretEncryptedTransitPayload CreateSharedSecretEncryptedPayload(ClientAccessToken token, object o)
        {
            var iv = ByteArrayUtil.GetRndByteArray(16);
            var key = token.SharedSecret;
            var jsonBytes = DotYouSystemSerializer.Serialize(o).ToUtf8ByteArray();
            // var encryptedBytes = AesCbc.Encrypt(jsonBytes, ref key, iv);
            var encryptedBytes = jsonBytes;

            var payload = new SharedSecretEncryptedTransitPayload()
            {
                Iv = iv,
                Data = Convert.ToBase64String(encryptedBytes)
            };

            return payload;
        }

        protected async Task<ClientAccessToken> ResolveClientAccessToken(OdinId recipient, ClientAccessTokenSource source)
        {
            // if (source == ClientAccessTokenSource.Circle)
            {
                var icr = await _circleNetworkService.GetIdentityConnectionRegistration(recipient);
                if (icr?.IsConnected() == false)
                {
                
                    return new ClientAccessToken()
                    {
                        Id = Guid.Empty,
                        AccessTokenHalfKey = Guid.Empty.ToByteArray().ToSensitiveByteArray(),
                        ClientTokenType = ClientTokenType.DataProvider,
                        SharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray(),
                    };
                    // throw new YouverseClientException("Cannot resolve client access token; not connected", YouverseClientErrorCode.NotAConnectedIdentity);
                }
                
                return icr!.CreateClientAccessToken();
            }

            // if (source == ClientAccessTokenSource.Follower)
            // {
            //     var def = await _followerService.GetFollower(recipient);
            //     if (null == def)
            //     {
            //         throw new YouverseClientException("Not a follower", YouverseClientErrorCode.NotAFollowerIdentity);
            //     }
            //     
            //     return def!.CreateClientAccessToken();
            // }
            //
            // if (source == ClientAccessTokenSource.IdentityIFollow)
            // {
            //     var def = await _followerService.GetIdentityIFollow(recipient);
            //     if (null == def)
            //     {
            //         throw new YouverseClientException("Identity is not followed", YouverseClientErrorCode.IdentityNotFollowed);
            //     }
            //     
            //     return def!.CreateClientAccessToken();
            // }
            //
            // throw new ArgumentException("Invalid ClientAccessTokenSource");
        }

        protected async Task<(ClientAccessToken token, ITransitHostReactionHttpClient client)> CreateReactionContentClient(OdinId odinId, ClientAccessTokenSource tokenSource,
            FileSystemType? fileSystemType = null)
        {
            var token = await ResolveClientAccessToken(odinId, tokenSource);

            var httpClient = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostReactionHttpClient>(
                odinId, token?.ToAuthenticationToken(), fileSystemType);

            return (token, httpClient);
        }
        
        protected async Task<T> DecryptUsingSharedSecret<T>(SharedSecretEncryptedTransitPayload payload, ClientAccessTokenSource tokenSource)
        {
            var caller = DotYouContext.Caller.OdinId;
            Guard.Argument(caller, nameof(DotYouContext.Caller.OdinId)).NotNull().Require(v => v.HasValue());

            //TODO: put decryption back in place
            // var t = await ResolveClientAccessToken(caller!.Value, tokenSource);
            // var sharedSecret = t.SharedSecret;
            // var encryptedBytes = Convert.FromBase64String(payload.Data);
            // var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref sharedSecret, payload.Iv);
         
            var decryptedBytes = Convert.FromBase64String(payload.Data);
            var json = decryptedBytes.ToStringFromUtf8Bytes();
            return await Task.FromResult(DotYouSystemSerializer.Deserialize<T>(json));
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