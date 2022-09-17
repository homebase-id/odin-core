using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistrationService : IYouAuthRegistrationService
    {
        private readonly ILogger<YouAuthRegistrationService> _logger;
        private readonly IYouAuthRegistrationStorage _youAuthRegistrationStorage;
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleDefinitionService _circleDefinitionService;

        public YouAuthRegistrationService(ILogger<YouAuthRegistrationService> logger, IYouAuthRegistrationStorage youAuthRegistrationStorage, ExchangeGrantService exchangeGrantService,
            ICircleNetworkService circleNetworkService, CircleDefinitionService circleDefinitionService)
        {
            _logger = logger;
            _youAuthRegistrationStorage = youAuthRegistrationStorage;
            _exchangeGrantService = exchangeGrantService;
            _circleNetworkService = circleNetworkService;
            _circleDefinitionService = circleDefinitionService;
        }

        //

        public ValueTask<ClientAccessToken> RegisterYouAuthAccess(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken)
        {
            if (string.IsNullOrWhiteSpace(dotYouId))
            {
                throw new YouAuthException("Invalid subject");
            }

            if (_circleNetworkService.TryCreateIdentityConnectionClient(dotYouId, remoteIcrClientAuthToken, out var icrClientAccessToken).GetAwaiter().GetResult())
            {
                return new ValueTask<ClientAccessToken>(icrClientAccessToken);
            }

            if (TryCreateAuthenticatedYouAuthClient(dotYouId, remoteIcrClientAuthToken, out ClientAccessToken youAuthClientAccessToken))
            {
                return new ValueTask<ClientAccessToken>(youAuthClientAccessToken);
            }

            throw new YouverseSecurityException("Unhandled case when registering YouAuth access");
        }


        /// <summary>
        /// Creates a YouAuth Client for an Identity that is not connected. (will show as authenticated)
        /// </summary>
        private bool TryCreateAuthenticatedYouAuthClient(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken, out ClientAccessToken clientAccessToken)
        {
            YouAuthRegistration registration = _youAuthRegistrationStorage.LoadFromSubject(dotYouId);
            
            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            if (null == registration)
            {
                registration = new YouAuthRegistration(dotYouId, new Dictionary<string, CircleGrant>());
                _youAuthRegistrationStorage.Save(registration);
            }

            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(emptyKey, ClientTokenType.YouAuth).GetAwaiter().GetResult();
            var client = new YouAuthClient(accessRegistration.Id, (DotYouIdentity)dotYouId, accessRegistration);
            _youAuthRegistrationStorage.SaveClient(client);

            
            clientAccessToken = cat;
            return true;
        }
        
        //

        public ValueTask<YouAuthRegistration?> LoadFromSubject(string subject)
        {
            var session = _youAuthRegistrationStorage.LoadFromSubject(subject);

            if (session != null)
            {
                _youAuthRegistrationStorage.Delete(session);
                session = null;
            }

            return new ValueTask<YouAuthRegistration?>(session);
        }

        //

        public ValueTask DeleteFromSubject(string subject)
        {
            var session = _youAuthRegistrationStorage.LoadFromSubject(subject);
            if (session != null)
            {
                _youAuthRegistrationStorage.Delete(session);
            }

            return new ValueTask();
        }

        public ValueTask<(DotYouIdentity dotYouId, bool isValid, bool isConnected, PermissionContext permissionContext, List<ByteArrayId> enabledCircleIds)> GetPermissionContext(
            ClientAuthenticationToken authToken)
        {
            if (authToken.ClientTokenType == ClientTokenType.YouAuth)
            {
                if (!this.ValidateClientAuthToken(authToken, out var client, out var registration))
                {
                    return new ValueTask<(DotYouIdentity, bool isValid, bool isConnected, PermissionContext permissionContext, List<ByteArrayId> enabledCircleIds)>(((DotYouIdentity)"", false, false,
                        null,
                        null));
                }

                var token = authToken.AccessTokenHalfKey;
                var accessKey = client.AccessRegistration.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);

                var permissionCtx = new PermissionContext(
                    new Dictionary<string, PermissionGroup>
                    {
                        {
                            "anonymous_drives", _exchangeGrantService.CreateAnonymousDrivePermissionGroup().GetAwaiter().GetResult()
                        }
                    },
                    sharedSecretKey: client.AccessRegistration.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey),
                    isOwner: false);

                return new ValueTask<(DotYouIdentity, bool, bool, PermissionContext, List<ByteArrayId>)>((client.DotYouId, true, false, permissionCtx, null));
            }

            if (authToken.ClientTokenType == ClientTokenType.IdentityConnectionRegistration)
            {
                var (dotYouId, isConnected, permissionContext, circleIds) = _circleNetworkService.CreateClientPermissionContext(authToken).GetAwaiter().GetResult();
                return new ValueTask<(DotYouIdentity dotYouId,
                    bool isValid,
                    bool isConnected,
                    PermissionContext permissionContext,
                    List<ByteArrayId> enabledCircleIds)>((
                    dotYouId, true, isConnected, permissionContext, circleIds));
            }

            throw new YouverseSecurityException("Unhandled access registration type");
        }

        private bool ValidateClientAuthToken(ClientAuthenticationToken authToken, out YouAuthClient client, out YouAuthRegistration registration)
        {
            client = _youAuthRegistrationStorage.GetYouAuthClient(authToken.Id);
            registration = null;

            var accessReg = client?.AccessRegistration;
            if (accessReg?.IsRevoked ?? true)
            {
                return false;
            }

            accessReg.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            registration = _youAuthRegistrationStorage.LoadFromSubject(client.DotYouId);
            if (null == registration)
            {
                return false;
            }

            return true;
        }
    }
}