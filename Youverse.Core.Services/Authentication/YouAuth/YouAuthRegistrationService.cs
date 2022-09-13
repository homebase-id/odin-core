using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
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

            YouAuthRegistration registration = _youAuthRegistrationStorage.LoadFromSubject(dotYouId);

            if (null == remoteIcrClientAuthToken)
            {
                return CreateEmptyClient(dotYouId, registration);
            }

            var icr = _circleNetworkService.GetIdentityConnectionRegistration(new DotYouIdentity(dotYouId), remoteIcrClientAuthToken).GetAwaiter().GetResult();
            if (!icr?.IsConnected() ?? false)
            {
                return CreateEmptyClient(dotYouId, registration);
            }

            // dotYouId is connected so let's upgrade the registration
            SensitiveByteArray grantKeyStoreKey;
            SymmetricKeyEncryptedAes icrRemoteKeyEncryptedKeyStoreKey = null;
            var accessTokenHalfKey = remoteIcrClientAuthToken.AccessTokenHalfKey;

            //Note: IcrRemoteKeyEncryptedKeyStoreKey can be null if an identity had logged in before being connected
            if (null == registration || registration.IcrRemoteKeyEncryptedKeyStoreKey == null)
            {
                grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
                icrRemoteKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref accessTokenHalfKey, ref grantKeyStoreKey);
            }
            else
            {
                grantKeyStoreKey = registration.IcrRemoteKeyEncryptedKeyStoreKey.DecryptKeyClone(ref accessTokenHalfKey);
            }

            // Convert the ICR's circle grants to new grants for a YouAuthRegistration
            var circleGrants = new Dictionary<string, CircleGrant>();
            foreach (var sourceGrant in icr!.AccessGrant.CircleGrants.Values)
            {
                var driveGrantRequests = sourceGrant.KeyStoreKeyEncryptedDriveGrants.Select(kdg => new DriveGrantRequest()
                {
                    PermissionedDrive = kdg.PermissionedDrive
                });

                var grant = _exchangeGrantService.CreateExchangeGrant(grantKeyStoreKey, sourceGrant.PermissionSet, driveGrantRequests, null).GetAwaiter().GetResult();

                circleGrants.Add(sourceGrant.CircleId.ToBase64(), new CircleGrant()
                {
                    CircleId = sourceGrant.CircleId,
                    KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
                    PermissionSet = grant.PermissionSet,
                });
            }

            registration = new YouAuthRegistration(dotYouId, circleGrants, icrRemoteKeyEncryptedKeyStoreKey!);
            _youAuthRegistrationStorage.Save(registration);

            var (accessRegistration, clientAccessToken) = _exchangeGrantService.CreateClientAccessToken(grantKeyStoreKey).GetAwaiter().GetResult();
            grantKeyStoreKey.Wipe();

            var client = new YouAuthClient(accessRegistration.Id, (DotYouIdentity)dotYouId, accessRegistration);
            _youAuthRegistrationStorage.SaveClient(client);

            return new ValueTask<ClientAccessToken>(clientAccessToken);
        }

        private ValueTask<ClientAccessToken> CreateEmptyClient(string dotYouId, YouAuthRegistration registration)
        {
            //TODO: this is fine until a user gets connected.  then that client needs to re-login.  I wonder if we can detect this
            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();

            if (null == registration)
            {
                registration = new YouAuthRegistration(dotYouId, new Dictionary<string, CircleGrant>(), null);
                _youAuthRegistrationStorage.Save(registration);
            }

            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(emptyKey).GetAwaiter().GetResult();

            var client = new YouAuthClient(accessRegistration.Id, (DotYouIdentity)dotYouId, accessRegistration);
            _youAuthRegistrationStorage.SaveClient(client);

            return new ValueTask<ClientAccessToken>(cat);
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

        //

        public ValueTask<(bool isValid, YouAuthClient? client, YouAuthRegistration registration)> ValidateClientAuthToken(ClientAuthenticationToken authToken)
        {
            var client = _youAuthRegistrationStorage.GetYouAuthClient(authToken.Id);
            var accessReg = client?.AccessRegistration;

            if (null == accessReg)
            {
                return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((false, null, null));
            }

            var registration = _youAuthRegistrationStorage.LoadFromSubject(client.DotYouId);

            if (null == registration)
            {
                return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((false, null, null));
            }

            //TODO: scan circles to see if any where revoked.  this way we don't have to wait until they've re-logged in to revoke access or maybe this is handled when a circle is changed, it should also update youauth

            if (accessReg.IsRevoked)
            {
                return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((false, null, null));
            }

            return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((true, client, registration));
        }

        //

        public ValueTask<(bool isConnected, PermissionContext permissionContext, List<ByteArrayId> enabledCircleIds)> GetPermissionContext(ClientAuthenticationToken authToken)
        {
            var (isValid, client, registration) = this.ValidateClientAuthToken(authToken).GetAwaiter().GetResult();

            if (!isValid)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            //Note: I'm having to override to get the icr here.  I wonder if we could encrypt the icrClientAuthToken on the youauth reg (encrypt with the you-auth-client's access reg
            var icr = _circleNetworkService.GetIdentityConnectionRegistration(new DotYouIdentity(registration.Subject), true).GetAwaiter().GetResult();
            var isConnected = icr?.IsConnected() ?? false;

            //Note: here we could compare the number icr.AccessGrant.CircleGrants and compare to those granted in youauth (below.
            // if they are different, we could force a logout and tell the user to log-in again

            var grants = new Dictionary<string, ExchangeGrant>();
            var enabledCircles = new List<ByteArrayId>();
            foreach (var kvp in registration.CircleGrants ?? new Dictionary<string, CircleGrant>())
            {
                var cg = kvp.Value;
                if (_circleDefinitionService.IsEnabled(cg.CircleId))
                {
                    enabledCircles.Add(cg.CircleId);
                    var xGrant = new ExchangeGrant()
                    {
                        Created = 0,
                        Modified = 0,
                        IsRevoked = false, //TODO
                        KeyStoreKeyEncryptedDriveGrants = cg.KeyStoreKeyEncryptedDriveGrants,
                        MasterKeyEncryptedKeyStoreKey = null,
                        PermissionSet = cg.PermissionSet
                    };
                    grants.Add(kvp.Key, xGrant);
                }
            }

            var permissionCtx = _exchangeGrantService.CreatePermissionContext(authToken, grants, client.AccessRegistration, false).GetAwaiter().GetResult();
            return new ValueTask<(bool, PermissionContext, List<ByteArrayId>)>((isConnected, permissionCtx, enabledCircles));
        }
    }
}