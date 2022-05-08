using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    //Need to consider using the recipient public key instead of the dotyouid
    //meaning i can go to frodo site, click connect and the public ke cert has all i need to
    //make the connect-request as well as encrypt the request.

    //see: DotYouClaimTypes.PublicKeyCertificate

    //can I get SAMs public key certificate from the request of the original client cert auth

    /// <summary>
    /// <inheritdoc cref="ICircleNetworkService"/>
    /// </summary>
    public class CircleNetworkService : ICircleNetworkService
    {
        const string CONNECTIONS = "cnncts";

        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ExchangeGrantService _exchangeGrantService;

        public CircleNetworkService(DotYouContextAccessor contextAccessor, ILogger<ICircleNetworkService> logger, ISystemStorage systemStorage, ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _exchangeGrantService = exchangeGrantService;
        }

        public async Task DeleteConnection(DotYouIdentity dotYouId)
        {
            _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Delete(dotYouId));
        }

        public async Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            if (info is {Status: ConnectionStatus.Connected})
            {
                info.Status = ConnectionStatus.None;
                _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<bool> Block(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<bool> Unblock(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Blocked)
            {
                info.Status = ConnectionStatus.Connected;
                _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<PagedResult<DotYouProfile>> GetBlockedProfiles(PageOptions req)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            var connectionsPage = await this.GetConnections(req, ConnectionStatus.Blocked);
            var page = new PagedResult<DotYouProfile>(
                connectionsPage.Request,
                connectionsPage.TotalPages,
                connectionsPage.Results.Select(c => new DotYouProfile()
                {
                    DotYouId = c.DotYouId,
                }).ToList());

            return page;
        }

        public async Task<PagedResult<DotYouProfile>> GetConnectedProfiles(PageOptions req)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            var connectionsPage = await this.GetConnections(req, ConnectionStatus.Connected);
            var page = new PagedResult<DotYouProfile>(
                connectionsPage.Request,
                connectionsPage.TotalPages,
                connectionsPage.Results.Select(c => new DotYouProfile()
                {
                    DotYouId = c.DotYouId,
                }).ToList());

            return page;
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, bool overrideHack = false)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING XTOKEN - REMOVE THIS
            if (!overrideHack)
            {
                _contextAccessor.GetCurrent().AssertCanManageConnections();
            }

            return await GetIdentityConnectionRegistrationInternal(dotYouId);
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, ClientAuthenticationToken remoteClientAuthenticationToken)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(dotYouId);

            if (connection?.AccessRegistrationId == null)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }

            // var accessReg = await _exchangeGrantService.GetAccessRegistration(connection.AccessRegistrationId);
            var accessReg = await _exchangeGrantService.GetAccessRegistration(remoteClientAuthenticationToken.Id);
            if (null == accessReg)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }

            accessReg.AssertValidRemoteKey(remoteClientAuthenticationToken.AccessTokenHalfKey);

            return connection;
        }

        public async Task<AccessRegistration> GetIdentityConnectionAccessRegistration(DotYouIdentity dotYouId, SensitiveByteArray remoteIdentityConnectionKey)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(dotYouId);

            if (connection?.AccessRegistrationId == null || connection?.IsConnected() == false)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }

            var accessReg = await _exchangeGrantService.GetAccessRegistration(connection.AccessRegistrationId);
            if (null == accessReg)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }

            accessReg.AssertValidRemoteKey(remoteIdentityConnectionKey);

            return accessReg;
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationWithKeyStoreKey(DotYouIdentity dotYouId, SensitiveByteArray exchangeRegistrationKsk)
        {
            throw new NotImplementedException();
            //
            // var connection = await GetIdentityConnectionRegistrationInternal(dotYouId);
            //
            // if (connection?.ExchangeRegistration == null)
            // {
            //     throw new YouverseSecurityException("Unauthorized Action");
            // }
            //
            // //TODO: make this an explicit assert
            // //this will fail if the xtoken and exchange registation are incorrect
            // connection.ExchangeRegistration.KeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref exchangeRegistrationKsk);
            //
            // return connection;
        }

        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternal(DotYouIdentity dotYouId)
        {
            var info = await _systemStorage.WithTenantSystemStorageReturnSingle<IdentityConnectionRegistration>(CONNECTIONS, s => s.Get(dotYouId));

            if (null == info)
            {
                return new IdentityConnectionRegistration()
                {
                    DotYouId = dotYouId,
                    Status = ConnectionStatus.None,
                    LastUpdated = -1
                };
            }

            return info;
        }

        public async Task<bool> IsConnected(DotYouIdentity dotYouId)
        {
            //allow the caller to see if s/he is connected, otherwise 
            if (_contextAccessor.GetCurrent().Caller.DotYouId != dotYouId)
            {
                //TODO: this needs to be changed to - can view connections
                _contextAccessor.GetCurrent().AssertCanManageConnections();
            }

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task AssertConnectionIsNoneOrValid(DotYouIdentity dotYouId)
        {
            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            this.AssertConnectionIsNoneOrValid(info);
        }

        public void AssertConnectionIsNoneOrValid(IdentityConnectionRegistration registration)
        {
            if (registration.Status == ConnectionStatus.Blocked)
            {
                throw new SecurityException("DotYouId is blocked");
            }
        }

        public async Task Connect(string dotYouIdentity, Guid accessRegistrationId, ClientAccessToken remoteClientAccessToken)
        {
            //TODO: need to add security that this method can be called

            var dotYouId = (DotYouIdentity) dotYouIdentity;

            //1. validate current connection state
            var info = await this.GetIdentityConnectionRegistrationInternal(dotYouId);

            if (info.Status != ConnectionStatus.None)
            {
                throw new YouverseSecurityException("invalid connection state");
            }

            await this.StoreConnection(dotYouId, accessRegistrationId, remoteClientAccessToken);
        }

        private async Task StoreConnection(string dotYouIdentity, Guid accessRegId, ClientAccessToken remoteClientAccessToken)
        {
            var dotYouId = (DotYouIdentity) dotYouIdentity;

            //2. add the record to the list of connections
            var newConnection = new IdentityConnectionRegistration()
            {
                DotYouId = dotYouId,
                Status = ConnectionStatus.Connected,
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AccessRegistrationId = accessRegId,
                ClientAccessTokenId = remoteClientAccessToken.Id,
                ClientAccessTokenHalfKey = remoteClientAccessToken.AccessTokenHalfKey.GetKey(),
                ClientAccessTokenSharedSecret = remoteClientAccessToken.SharedSecret.GetKey()
            };

            _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(newConnection));

            //TODO: the following is a good place for the mediatr pattern
            //tell the profile service to refresh the attributes?
            //send notification to clients
        }

        private async Task<PagedResult<IdentityConnectionRegistration>> GetConnections(PageOptions req, ConnectionStatus status)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Expression<Func<IdentityConnectionRegistration, string>> sortKeySelector = key => key.DotYouId;
            Expression<Func<IdentityConnectionRegistration, bool>> predicate = id => id.Status == status;
            PagedResult<IdentityConnectionRegistration> results = await _systemStorage.WithTenantSystemStorageReturnList<IdentityConnectionRegistration>(CONNECTIONS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
            return results;
        }
    }
}