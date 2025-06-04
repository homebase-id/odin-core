#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Core.Storage;
using Odin.Core.Storage.Cache;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeCachingService : INotificationHandler<IDriveNotification>, INotificationHandler<DriveDefinitionAddedNotification>
    {
#if DEBUG
        public static int CacheMiss;
#endif
        private readonly FileSystemHttpRequestResolver _fsResolver;

        private readonly OdinConfiguration _config;
        private readonly IDriveManager _driveManager;

        public const int PostFileType = 101;
        public const int ChannelFileType = 103;

        private readonly int[] _fileTypesCausingCacheReset = { PostFileType, ChannelFileType };
        private readonly ITenantLevel1Cache<HomeCachingService> _cache;

        public HomeCachingService(
            IDriveManager driveManager,
            OdinConfiguration config,
            FileSystemHttpRequestResolver fsResolver,
            ITenantLevel1Cache<HomeCachingService> cache)
        {
            _driveManager = driveManager;
            _config = config;
            _fsResolver = fsResolver;
            _cache = cache;
        }

        //

        public async Task<QueryBatchCollectionResponse> GetResult(QueryBatchCollectionRequest request,
            IOdinContext odinContext, OdinId tenantOdinId)
        {
            var key = GetCacheKey(string.Join("-", request.Queries.Select(q => q.Name)), tenantOdinId);
            var result = await _cache.GetOrSetAsync(key, _ =>
                {
#if DEBUG
                    Interlocked.Increment(ref CacheMiss);
#endif
                    return _fsResolver.ResolveFileSystem().Query.GetBatchCollection(request, odinContext);
                },
                TimeSpan.FromSeconds(_config.Host.HomePageCachingExpirationSeconds),
                CacheTags);

            return result!;
        }

        //

        public async Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            if (notification.Drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType)
            {
                await InvalidateAsync();
            }
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var drive = await _driveManager.GetDriveAsync(notification.File.DriveId);
            if (null == drive)
            {
                //just invalidate because the drive might have been deleted for some reason
                await InvalidateAsync();
                return;
            }

            if (drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType)
            {
                var header = notification.ServerFileHeader;
                if (header.ServerMetadata.FileSystemType == FileSystemType.Standard)
                {
                    if (_fileTypesCausingCacheReset.Any(fileType => header.FileMetadata.AppData.FileType == fileType))
                    {
                        await InvalidateAsync();
                    }
                }
            }
        }

        private string GetCacheKey(string key, OdinId tenantOdinId)
        {
            return $"{tenantOdinId}-{key}";
        }

        private List<string> CacheTags { get; } = [nameof(HomeCachingService)];

        public async Task InvalidateAsync()
        {
            await _cache.RemoveByTagAsync(CacheTags);
        }

#if DEBUG
        public static void ResetCacheStats()
        {
            Interlocked.Exchange(ref CacheMiss, 0);
        }
#endif
    }
}