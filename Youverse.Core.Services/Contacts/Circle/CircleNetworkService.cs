using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Authorization.Exchange;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Profile;

namespace Youverse.Core.Services.Contacts.Circle
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

        private readonly IProfileService _profileService;
        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContextAccessor _contextAccessor;

        public CircleNetworkService(DotYouContextAccessor contextAccessor, IProfileService profileService, ILogger<ICircleNetworkService> logger, ISystemStorage systemStorage)
        {
            _contextAccessor = contextAccessor;
            _profileService = profileService;
            _systemStorage = systemStorage;
        }

        public async Task DeleteConnection(DotYouIdentity dotYouId)
        {
            _systemStorage.WithTenantSystemStorage<ConnectionInfo>(CONNECTIONS, s => s.Delete(dotYouId));
        }

        public async Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetConnectionInfo(dotYouId);
            if (info is {Status: ConnectionStatus.Connected})
            {
                info.Status = ConnectionStatus.None;
                _systemStorage.WithTenantSystemStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<bool> Block(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetConnectionInfo(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                _systemStorage.WithTenantSystemStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<bool> Unblock(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetConnectionInfo(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Blocked)
            {
                info.Status = ConnectionStatus.Connected;
                _systemStorage.WithTenantSystemStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<PagedResult<ConnectionInfo>> GetConnections(PageOptions req)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Expression<Func<ConnectionInfo, string>> sortKeySelector = key => key.DotYouId;
            Expression<Func<ConnectionInfo, bool>> predicate = id => id.Status == ConnectionStatus.Connected;
            PagedResult<ConnectionInfo> results = await _systemStorage.WithTenantSystemStorageReturnList<ConnectionInfo>(CONNECTIONS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
            return results;
        }

        public async Task<PagedResult<ConnectionInfo>> GetBlockedConnections(PageOptions req)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Expression<Func<ConnectionInfo, string>> sortKeySelector = key => key.DotYouId;
            Expression<Func<ConnectionInfo, bool>> predicate = id => id.Status == ConnectionStatus.Blocked;
            PagedResult<ConnectionInfo> results = await _systemStorage.WithTenantSystemStorageReturnList<ConnectionInfo>(CONNECTIONS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
            return results;
        }

        public async Task<PagedResult<DotYouProfile>> GetBlockedProfiles(PageOptions req)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            //HACK: this method of joining the connection info class to the profiles is very error prone.  Need to rewrite when I pull a sql db
            var connections = await GetBlockedConnections(req);

            var list = new List<DotYouProfile>();
            foreach (var conn in connections.Results)
            {
                var profile = await _profileService.Get(conn.DotYouId);
                if (null != profile)
                {
                    list.Add(profile);
                }
            }

            var results = new PagedResult<DotYouProfile>(req, connections.TotalPages, list);

            return results;
        }

        public async Task<PagedResult<DotYouProfile>> GetConnectedProfiles(PageOptions req)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            //HACK: this method of joining the connection info class to the profiles is very error prone.  Need to rewrite when I pull a sql db
            var connections = await GetConnections(req);

            var list = new List<DotYouProfile>();
            foreach (var conn in connections.Results)
            {
                var profile = await _profileService.Get(conn.DotYouId);
                if (null != profile)
                {
                    list.Add(profile);
                }
            }

            var results = new PagedResult<DotYouProfile>(req, connections.TotalPages, list);

            return results;
        }

        public async Task<ConnectionInfo> GetConnectionInfo(DotYouIdentity dotYouId, bool overrideHack = false)
        {
            //HACK: DOING THIS WHILE DESIGNING XTOKEN - REMOVE THIS
            if(!overrideHack)
            {
                _contextAccessor.GetCurrent().AssertCanManageConnections();
            }
            
            return await GetConnectionInfoInternal(dotYouId);
        }

        public async Task<ConnectionInfo> GetConnectionInfo(DotYouIdentity dotYouId, SensitiveByteArray xTokenHalfKey)
        {
            var connection = await GetConnectionInfoInternal(dotYouId);

            if (connection?.XToken == null)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }

            connection.XToken.AssertValidHalfKey(xTokenHalfKey);

            return connection;
        }

        private async Task<ConnectionInfo> GetConnectionInfoInternal(DotYouIdentity dotYouId)
        {
            var info = await _systemStorage.WithTenantSystemStorageReturnSingle<ConnectionInfo>(CONNECTIONS, s => s.Get(dotYouId));

            if (null == info)
            {
                return new ConnectionInfo()
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
            //TODO: this needs to be changed to - can view connections
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetConnectionInfo(dotYouId);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task AssertConnectionIsNoneOrValid(DotYouIdentity dotYouId)
        {
            var info = await this.GetConnectionInfo(dotYouId);
            this.AssertConnectionIsNoneOrValid(info);
        }

        public void AssertConnectionIsNoneOrValid(ConnectionInfo info)
        {
            if (info.Status == ConnectionStatus.Blocked)
            {
                throw new SecurityException("DotYouId is blocked");
            }
        }

        public async Task Connect(string dotYouIdentity, NameAttribute name, XToken xtoken, SensitiveByteArray remoteGrantKey, SensitiveByteArray remoteSharedSecret)
        {
            //TODO: need to add securitry that this method can be called

            var dotYouId = (DotYouIdentity) dotYouIdentity;

            //1. validate current connection state
            var info = await this.GetConnectionInfoInternal(dotYouId);
            
            if (info.Status != ConnectionStatus.None)
            {
                throw new YouverseSecurityException("invalid connection state");
            }

            await this.StoreConnection(dotYouId, name, xtoken, remoteGrantKey, remoteSharedSecret);

        }

        private async Task StoreConnection(string dotYouIdentity, NameAttribute name, XToken xtoken, SensitiveByteArray remoteGrantKey, SensitiveByteArray remoteSharedSecret)
        {
            var dotYouId = (DotYouIdentity) dotYouIdentity;

            //2. add the record to the list of connections
            var newConnection = new ConnectionInfo()
            {
                DotYouId = dotYouId,
                Status = ConnectionStatus.Connected,
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                XToken = xtoken,
                RemoteGrantKey = remoteGrantKey.GetKey(),
                RemoteSharedSecret = remoteSharedSecret.GetKey()
            };

            _systemStorage.WithTenantSystemStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(newConnection));

            //3. upsert any record in the profile service (we upsert just in case there was previously a connection)

            var contact = new DotYouProfile()
            {
                Name = name,
                DotYouId = dotYouId,
                SslPublicKeyCertificate = dotYouId, //using Sender here because it will be the original person to which I sent the request.
            };

            await _profileService.Save(contact);

            //TODO: the following is a good place for the mediatr pattern
            //tell the profile service to refresh the attributes?
            //send notification to clients
        }
    }
}