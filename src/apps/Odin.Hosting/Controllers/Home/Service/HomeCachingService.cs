#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
using LazyCache.Providers;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Identity;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;
using Odin.Services.Tenant;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeCachingService : INotificationHandler<IDriveNotification>, INotificationHandler<DriveDefinitionAddedNotification>
    {
#if DEBUG
        public static int CacheMiss;
#endif
        private readonly FileSystemHttpRequestResolver _fsResolver;

        private readonly OdinConfiguration _config;
        private readonly DriveManager _driveManager;

        public const int PostFileType = 101;
        public const int ChannelFileType = 103;

        private readonly int[] _fileTypesCausingCacheReset = { PostFileType, ChannelFileType };

        private IAppCache? _cache;

        public HomeCachingService(DriveManager driveManager, OdinConfiguration config,
            FileSystemHttpRequestResolver fsResolver)
        {
            _driveManager = driveManager;
            _config = config;
            _fsResolver = fsResolver;

            InitializeCache();
        }

        //

        public async Task<QueryBatchCollectionResponse> GetResult(QueryBatchCollectionRequest request, IOdinContext odinContext, OdinId tenantOdinId, IdentityDatabase db)
        {
            var queryBatchCollection = new Func<Task<QueryBatchCollectionResponse>>(async delegate
            {
#if DEBUG
                CacheMiss++;
#endif
                var collection = await _fsResolver.ResolveFileSystem().Query.GetBatchCollection(request, odinContext, db);
                return collection;
            });

            var key = GetCacheKey(string.Join("-", request.Queries.Select(q => q.Name)), tenantOdinId);
            var policy = new MemoryCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromSeconds(_config.Host.HomePageCachingExpirationSeconds),
            };

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

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var drive = await _driveManager.GetDriveAsync(notification.File.DriveId, notification.db);
            if (null == drive)
            {
                //just invalidate because the drive might have been deleted for some reason
                Invalidate();
                return;
            }

            if (drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType)
            {
                var header = notification.ServerFileHeader;
                if (header.ServerMetadata.FileSystemType == FileSystemType.Standard)
                {
                    if (_fileTypesCausingCacheReset.Any(fileType => header.FileMetadata.AppData.FileType == fileType))
                    {
                        Invalidate();
                    }
                }
            }
        }

        private string GetCacheKey(string key, OdinId tenantOdinId)
        {
            return $"{tenantOdinId}-{key}";
        }

        public void Invalidate()
        {
            //from: https://github.com/alastairtree/LazyCache/wiki/API-documentation-(v-2.x)#empty-the-entire-cache
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