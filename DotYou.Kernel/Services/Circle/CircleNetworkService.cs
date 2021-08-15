using Dawn;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Security;
using DotYou.Types;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Contacts;
using DotYou.Kernel.Services.Identity;
using DotYou.Types.DataAttribute;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace DotYou.Kernel.Services.Circle
{
    //Need to consider using the recipient public key instead of the dotyouid
    //meaning i can go to frodo site, click connect and the public ke cert has all i need to
    //make the connect-request as well as encrypt the request.

    //see: DotYouClaimTypes.PublicKeyCertificate

    //can I get SAMs public key certificate from the request of the original client cert auth

    /// <summary>
    /// <inheritdoc cref="ICircleNetworkService"/>
    /// </summary>
    public class CircleNetworkService : DotYouServiceBase, ICircleNetworkService
    {
        const string CONNECTIONS = "cnncts";

        private readonly IProfileService _profileService;

        public CircleNetworkService(DotYouContext context, IProfileService profileService, ILogger<CircleNetworkService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac) : base(context, logger, hub, fac)
        {
            _profileService = profileService;
        }

        public async Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            var info = await this.GetConnectionInfo(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.None;
                WithTenantStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<bool> Block(DotYouIdentity dotYouId)
        {
            var info = await this.GetConnectionInfo(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                WithTenantStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<bool> Unblock(DotYouIdentity dotYouId)
        {
            var info = await this.GetConnectionInfo(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Blocked)
            {
                info.Status = ConnectionStatus.Connected;
                WithTenantStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<PagedResult<ConnectionInfo>> GetConnections(PageOptions req)
        {
            Expression<Func<ConnectionInfo, string>> sortKeySelector = key => key.DotYouId;
            Expression<Func<ConnectionInfo, bool>> predicate = id => id.Status == ConnectionStatus.Connected;
            PagedResult<ConnectionInfo> results = await WithTenantStorageReturnList<ConnectionInfo>(CONNECTIONS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
            return results;
        }

        public async Task<PagedResult<ConnectionInfo>> GetBlockedConnections(PageOptions req)
        {
            Expression<Func<ConnectionInfo, string>> sortKeySelector = key => key.DotYouId;
            Expression<Func<ConnectionInfo, bool>> predicate = id => id.Status == ConnectionStatus.Blocked;
            PagedResult<ConnectionInfo> results = await WithTenantStorageReturnList<ConnectionInfo>(CONNECTIONS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
            return results;
        }
        
        public async Task<PagedResult<DotYouProfile>> GetBlockedProfiles(PageOptions req)
        {
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

        public async Task<ConnectionInfo> GetConnectionInfo(DotYouIdentity dotYouId)
        {
            var info = await WithTenantStorageReturnSingle<ConnectionInfo>(CONNECTIONS, s => s.Get(dotYouId));

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

        public async Task Connect(string publicKeyCertificate, NameAttribute name)
        {
            var cert = new DomainCertificate(publicKeyCertificate);
            var dotYouId = cert.DotYouId;

            //1. validate current connection state
            await AssertConnectionIsNoneOrValid(dotYouId);
            var info = await this.GetConnectionInfo(dotYouId);
            if (info.Status == ConnectionStatus.Connected)
            {
                return;
            }

            //2. add the record to the list of connections
            var newConnection = new ConnectionInfo()
            {
                DotYouId = dotYouId,
                Status = ConnectionStatus.Connected,
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            WithTenantStorage<ConnectionInfo>(CONNECTIONS, s => s.Save(newConnection));

            //3. upsert any record in the profile service (we upsert just in case there was previously a connection)
            
            var contact = new DotYouProfile()
            {
                Name = name,
                DotYouId = cert.DotYouId,
                PublicKeyCertificate = publicKeyCertificate, //using Sender here because it will be the original person to which I sent the request.
            };

            await _profileService.Save(contact);

            //TODO: the following is a good place for the mediatr pattern
            //tell the profile service to refresh the attributes?
            //send notification to clients
        }
    }
}