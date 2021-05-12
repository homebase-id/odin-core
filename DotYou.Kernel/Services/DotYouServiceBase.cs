using DotYou.Kernel.Storage;
using DotYou.Types;
using Identity.Web.Services.Contacts;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types.ApiClient;
using DotYou.Types.Circle;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace DotYou.Kernel.Services
{
    /// <summary>
    /// Base class for all services offering <see cref="IStorage<T>"/> and 
    /// <see cref="IDotYouHttpClientProxy"/> instances based on 
    /// the specified <see cref="DotYouContext"/>.
    /// </summary>
    public abstract class DotYouServiceBase
    {
        ILogger _logger;
        private readonly IDotYouHttpClientProxy _httpProxy;
        private readonly DotYouContext _context;
        private readonly IHubContext<NotificationHub, INotificationHub> _notificationHub;


        protected DotYouServiceBase(DotYouContext context, ILogger logger, IHubContext<NotificationHub, INotificationHub> notificationHub)
        {
            _logger = logger;
            _notificationHub = notificationHub;
            _context = context;
            var proxy = new DotYouHttpClientProxy(context);
            _httpProxy = proxy;
        }

        /// <summary>
        /// Logger instance
        /// </summary>
        protected ILogger Logger
        {
            get => _logger;
        }

        /// <summary>
        /// The context for a given <see cref="DotYouIdentity"/>
        /// </summary>
        protected DotYouContext Context
        {
            get => _context;
        }

        /// <summary>
        /// Proxy which makes calls to other <see cref="DotYouIdentity"/> servers using a pre-configured HttpClient.
        /// </summary>
        protected IDotYouHttpClientProxy HttpProxy
        {
            get => _httpProxy;
        }

       
        protected void WithTenantStorage<T>(string collection, Action<LiteDBSingleCollectionStorage<T>> action)
        {
            var cfg = _context.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                action(storage);
            }
        }

        protected Task<PagedResult<T>> WithTenantStorageReturnList<T>(string collection, Func<LiteDBSingleCollectionStorage<T>, Task<PagedResult<T>>> func)
        {
            var cfg = _context.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                return func(storage);
            }
        }

        protected Task<T> WithTenantStorageReturnSingle<T>(string collection, Func<LiteDBSingleCollectionStorage<T>, Task<T>> func)
        {
            var cfg = _context.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                return func(storage);
            }
        }
    }

    public class NotificationHub : Hub<INotificationHub>
    {
        // public Task NotifyOfCircleInvite(CircleInvite circleInvite)
        // {
        //     Clients.All.NotificationOfCircleInvite(circleInvite);
        //
        //     return Task.CompletedTask;
        // }

        public Task NotifyOfConnectionRequest(ConnectionRequest request)
        {
            Clients.All.NotifyOfConnectionRequest(request);
            return Task.CompletedTask;
        }

        public Task NotifyOfConnectionRequestAccepted(EstablishConnectionRequest acceptedRequest)
        {
            Clients.All.NotifyOfConnectionRequestAccepted(acceptedRequest);
            return Task.CompletedTask;
        }
    }
}