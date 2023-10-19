#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
using LazyCache.Providers;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeCachingService : INotificationHandler<IDriveNotification>, INotificationHandler<DriveDefinitionAddedNotification>
    {
#if DEBUG
        public static int CacheMiss = 0;
#endif
        private readonly OdinContextAccessor _contextAccessor;
        private readonly FileSystemHttpRequestResolver _fsResolver;

        private readonly OdinConfiguration _config;
        private readonly DriveManager _driveManager;

        public const int PostFileType = 101;
        public const int ChannelFileType = 103;

        private readonly int[] _fileTypesCausingCacheReset = { PostFileType, ChannelFileType };

        private IAppCache _cache;
        private readonly CancellationTokenSource _expiryTokenSource = new();

        public HomeCachingService(DriveManager driveManager, OdinConfiguration config, OdinContextAccessor contextAccessor, FileSystemHttpRequestResolver fsResolver)
        {
            _driveManager = driveManager;
            _config = config;
            _contextAccessor = contextAccessor;
            _fsResolver = fsResolver;

            InitializeCache();
        }

        //

        public async Task<QueryBatchCollectionResponse> GetResult(QueryBatchCollectionRequest request)
        {
            var queryBatchCollection = new Func<Task<QueryBatchCollectionResponse>>(async delegate
            {
#if DEBUG
                CacheMiss++;
#endif
                var collection = await _fsResolver.ResolveFileSystem().Query.GetBatchCollection(request);
                return collection;
            });

            var key = GetCacheKey(string.Join("-", request.Queries.Select(q => q.Name)));
            var policy = new MemoryCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromSeconds(_config.Host.HomePageCachingExpirationSeconds),
            };

            policy.AddExpirationToken(new CancellationChangeToken(_expiryTokenSource.Token));
            return await _cache.GetOrAddAsync(key, queryBatchCollection, policy);
        }

        //

        public Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            if (notification.Drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType)
            {
                Invalidate();
            }
            
            return Task.CompletedTask;
        }

        public Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var drive = _driveManager.GetDrive(notification.File.DriveId).GetAwaiter().GetResult();
            if (null == drive)
            {
                //just invalidate because the drive might have been deleted for some reason
                Invalidate();
                return Task.CompletedTask;
            }

            if (drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType)
            {
                var header = notification.ServerFileHeader;
                if (header.ServerMetadata.FileSystemType == FileSystemType.Standard)
                {
                    if (_fileTypesCausingCacheReset.Any(fileType => header.FileMetadata.AppData.FileType == fileType))
                    {
                        Invalidate();
                        return Task.CompletedTask;
                    }
                }
            }

            return Task.CompletedTask;
        }

        private string GetCacheKey(string key)
        {
            return $"{_contextAccessor.GetCurrent().Tenant}-{key}";
        }

        public void Invalidate()
        {
            //from: https://github.com/alastairtree/LazyCache/wiki/API-documentation-(v-2.x)#empty-the-entire-cache
            // _expiryTokenSource.Cancel();

            InitializeCache();
        }

        private void InitializeCache()
        {
            var provider = new MemoryCacheProvider(
                new MemoryCache(
                    new MemoryCacheOptions()
                    {
                        ExpirationScanFrequency = TimeSpan.FromMinutes(2),
                        // SizeLimit = ???
                    }));
            _cache = new CachingService(provider);
        }

#if DEBUG
        public static void ResetCacheStats()
        {
            CacheMiss = 0;
        }
#endif
    }
}