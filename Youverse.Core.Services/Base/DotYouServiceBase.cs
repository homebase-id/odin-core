using System;
using System.Security;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Notifications;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Base class for all services offering <see cref="IStorage<T>"/> and 
    /// <see cref="IDotYouHttpClientProxy"/> instances based on 
    /// the specified <see cref="DotYouContext"/>.
    /// </summary>
    public abstract class DotYouServiceBase<T>
    {
        private readonly ILogger<T> _logger;
        private readonly DotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly DotYouContext _context;
        private NotificationHandler _notificationHandler;
        
        protected DotYouServiceBase(DotYouContext context, ILogger<T> logger, NotificationHandler notificationHub, DotYouHttpClientFactory fac)
        {
            _logger = logger;
            _context = context;
            _dotYouHttpClientFactory = fac;
            _notificationHandler = notificationHub;
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
        protected T CreatePerimeterHttpClient<T>(DotYouIdentity dotYouId, string appIdOverride = null)
        {
            Guard.Argument(_dotYouHttpClientFactory, nameof(_dotYouHttpClientFactory)).NotNull("The derived class did not initialize the http client factory.");
            return _dotYouHttpClientFactory.CreateClient<T>(dotYouId, appIdOverride);
        }

        protected NotificationHandler Notify
        {
            get => null; // _notificationHandler.Clients.User(this.Context.HostDotYouId);
        }

        protected void AssertCallerIsOwner()
        {
            if (this.Context.Caller.IsOwner == false)
            {
                throw new SecurityException("Caller must be owner");
            }
        }

        protected void WithTenantSystemStorage<T>(string collection, Action<LiteDBSingleCollectionStorage<T>> action)
        {
            var cfg = _context.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                action(storage);
            }
        }

        protected Task<PagedResult<T>> WithTenantSystemStorageReturnList<T>(string collection, Func<LiteDBSingleCollectionStorage<T>, Task<PagedResult<T>>> func)
        {
            var cfg = _context.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                return func(storage);
            }
        }

        protected Task<T> WithTenantSystemStorageReturnSingle<T>(string collection, Func<LiteDBSingleCollectionStorage<T>, Task<T>> func)
        {
            var cfg = _context.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                return func(storage);
            }
        }
    }
}