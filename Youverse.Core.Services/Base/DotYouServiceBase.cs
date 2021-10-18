using DotYou.Kernel.Storage;
using DotYou.Types;
using Microsoft.Extensions.Logging;
using System;
using System.Security;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
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
        private readonly DotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly DotYouContext _context;
        private readonly IHubContext<NotificationHub, INotificationHub> _notificationHub;
        
        protected DotYouServiceBase(DotYouContext context, ILogger logger, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac)
        {
            _logger = logger;
            _notificationHub = notificationHub;
            _context = context;
            _dotYouHttpClientFactory = fac;
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
        protected internal IPerimeterHttpClient CreatePerimeterHttpClient(DotYouIdentity dotYouId)
        {
            Guard.Argument(_dotYouHttpClientFactory, nameof(_dotYouHttpClientFactory)).NotNull("The derived class did not initialize the http client factory.");
            return _dotYouHttpClientFactory.CreateClient(dotYouId);
        }

        /// <summary>
        /// Creates a proxy client of T 
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected T CreatePerimeterHttpClient<T>(DotYouIdentity dotYouId)
        {
            Guard.Argument(_dotYouHttpClientFactory, nameof(_dotYouHttpClientFactory)).NotNull("The derived class did not initialize the http client factory.");
            return _dotYouHttpClientFactory.CreateClient<T>(dotYouId);
        }

        protected INotificationHub Notify
        {
            get => _notificationHub.Clients.User(this.Context.HostDotYouId);
        }
        
        protected void AssertCallerIsOwner()
        {
            if (this.Context.Caller.IsOwner == false)
            {
                throw new SecurityException("Caller must be owner");
            }
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
}