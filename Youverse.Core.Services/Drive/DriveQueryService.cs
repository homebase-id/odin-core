using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Drive
{
    public class DriveQueryService : IDriveQueryService, INotificationHandler<DriveFileChangedNotification>
    {
        private readonly IDriveService _driveService;
        private readonly ConcurrentDictionary<Guid, IDriveQueryManager> _queryManagers;
        private readonly IAuthorizationService _authorizationService;
        private readonly ILoggerFactory _loggerFactory;

        public DriveQueryService(IDriveService driveService, ILoggerFactory loggerFactory, IAuthorizationService authorizationService)
        {
            _driveService = driveService;
            _loggerFactory = loggerFactory;
            _authorizationService = authorizationService;
            _queryManagers = new ConcurrentDictionary<Guid, IDriveQueryManager>();
            
            InitializeQueryManagers();
        }
        
        public Task RebuildAllIndices()
        {
            //TODO: optimize by making this parallel processed or something
            foreach (var qm in _queryManagers.Values)
            {
                //intentionally not awaiting the result
                this.RebuildBackupIndex(qm.Drive.Id);
            }

            return Task.CompletedTask;
        }
        
        public async Task RebuildBackupIndex(Guid driveId)
        {
            //TODO: add looping for paging so we work in chunks instead of all files at once.
            var paging = PageOptions.All;
            var metaDataList = await _driveService.GetMetadataFiles(driveId, paging);

            await this.TryGetOrLoadQueryManager(driveId, out var manager, false);
            await manager.PrepareSecondaryIndexForRebuild();
            foreach (FileMetadata md in metaDataList)
            {
                //intentionally letting this run w/o await
                manager.UpdateSecondaryIndex(md);
            }

            await manager.SwitchIndex();
        }

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, PageOptions pageOptions)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var page =  await queryManager.GetRecentlyCreatedItems(includeContent, pageOptions);
                return ApplySecurityFiltering(page);
            }

            throw new NoValidIndexException(driveId);
        }
        
        public async Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, PageOptions pageOptions)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var page =  await queryManager.GetItemsByCategory(categoryId, includeContent, pageOptions);
                return ApplySecurityFiltering(page);
            }

            throw new NoValidIndexException(driveId);
        }

        private PagedResult<IndexedItem> ApplySecurityFiltering(PagedResult<IndexedItem> fullResults)
        {
            var filtered = fullResults.Results.Where(item => _authorizationService.CallerHasPermission(item.AccessControlList).GetAwaiter().GetResult()).ToList();
            return new PagedResult<IndexedItem>(fullResults.Request, fullResults.TotalPages, filtered);
        }
        
        private async void InitializeQueryManagers()
        {
            var allDrives = await _driveService.GetDrives(new PageOptions(1, Int32.MaxValue));
            foreach (var drive in allDrives.Results)
            {
                await this.LoadQueryManager(drive, out var _);
            }
        }

        private async Task RebuildCurrentIndex(Guid driveId)
        {
            //TODO: add looping for paging so we work in chunks instead of all files at once.
            var paging = PageOptions.All;
            var metaDataList = await _driveService.GetMetadataFiles(driveId, paging);

            await this.TryGetOrLoadQueryManager(driveId, out var manager, false);
            manager.IndexReadyState = IndexReadyState.IsRebuilding;

            foreach (FileMetadata md in metaDataList)
            {
                //intentionally letting this run w/o await
                manager.UpdateCurrentIndex(md);
            }

            manager.IndexReadyState = IndexReadyState.Ready;
        }

        private Task<bool> TryGetOrLoadQueryManager(Guid driveId, out IDriveQueryManager manager, bool onlyReadyManagers = true)
        {
            if (_queryManagers.TryGetValue(driveId, out manager))
            {
                if (onlyReadyManagers && manager.IndexReadyState != IndexReadyState.Ready)
                {
                    manager = null;
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }

            var drive = _driveService.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            LoadQueryManager(drive, out manager);

            if (onlyReadyManagers && manager.IndexReadyState != IndexReadyState.Ready)
            {
                manager = null;
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private Task LoadQueryManager(StorageDrive drive, out IDriveQueryManager manager)
        {
            var logger = _loggerFactory.CreateLogger<IDriveQueryManager>();
            manager = new LiteDbDriveQueryManager(drive, logger);

            //add it first in case load latest fails.  we want to ensure the
            //rebuild process can still access this manager to rebuild its index
            _queryManagers.TryAdd(drive.Id, manager);
            manager.LoadLatestIndex().GetAwaiter().GetResult();
            if (manager.IndexReadyState == IndexReadyState.RequiresRebuild)
            {
                //start a rebuild in the background for this index
                //this.RebuildCurrentIndex(drive.Id);
            }

            return Task.CompletedTask;
        }

        public Task Handle(DriveFileChangedNotification notification, CancellationToken cancellationToken)
        {
            this.TryGetOrLoadQueryManager(notification.File.DriveId, out var manager, false);
            return manager.UpdateCurrentIndex(notification.FileMetadata);           
        }
    }
}