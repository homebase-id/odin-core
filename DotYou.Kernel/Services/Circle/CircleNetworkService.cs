using Dawn;
using System;
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

    public enum ConnectionStatus
    {
        None = 1,
        Connected = 2,
        Blocked = 3
    }

    public class ConnectionInfo
    {
        public DotYouIdentity DotYouId { get; set; }
        public ConnectionStatus Status { get; set; }
        public long LastUpdated { get; set; }
    }

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
        
        public Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> Block(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> Unblock(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }

        public async Task<PagedResult<ConnectionInfo>> GetConnections(PageOptions req)
        {
            Expression<Func<ConnectionInfo, string>> sortKeySelector = key => key.DotYouId;
            Expression<Func<ConnectionInfo, bool>> predicate = id => id.Status == ConnectionStatus.Connected;
            PagedResult<ConnectionInfo> results = await WithTenantStorageReturnList<ConnectionInfo>(CONNECTIONS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
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
                    LastUpdated = 0
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

            var ec = await _profileService.Get(dotYouId);

            var contact = new HumanProfile()
            {
                Id = ec?.Id ?? Guid.NewGuid(),
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