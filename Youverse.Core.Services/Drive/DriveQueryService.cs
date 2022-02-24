using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Drive
{
    public class DriveQueryService : IDriveQueryService, INotificationHandler<DriveFileChangedNotification>
    {
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        private readonly ConcurrentDictionary<Guid, IDriveQueryManager> _queryManagers;
        private readonly IDriveAclAuthorizationService _driveAclAuthorizationService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IHttpContextAccessor _accessor;

        public DriveQueryService(IDriveService driveService, ILoggerFactory loggerFactory, IDriveAclAuthorizationService driveAclAuthorizationService, DotYouContext context, IHttpContextAccessor accessor=null)
        {
            _driveService = driveService;
            _loggerFactory = loggerFactory;
            _driveAclAuthorizationService = driveAclAuthorizationService;
            _accessor = accessor;
            //_context = context;

            _context = accessor.HttpContext.RequestServices.GetRequiredService<DotYouContext>();

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

        public async Task<PagedResult<DriveSearchResult>> GetRecentlyCreatedItems(Guid driveId, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var page = await queryManager.GetRecentlyCreatedItems(includeMetadataHeader, pageOptions, _driveAclAuthorizationService);
                return await CreateSearchResult(driveId, page, includePayload);
            }

            throw new NoValidIndexException(driveId);
        }

        public async Task<PagedResult<DriveSearchResult>> GetByTag(Guid driveId, Guid tag, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager, false))
            {
                //HACK; need to figure out what it means for an index to be valid or not
                if (queryManager.IndexReadyState == IndexReadyState.Ready)
                {
                    var page = await queryManager.GetByTag(tag, includeMetadataHeader, pageOptions, _driveAclAuthorizationService);
                    var pageResult = await CreateSearchResult(driveId, page, includePayload);
                    return pageResult;
                }

                return new PagedResult<DriveSearchResult>(pageOptions, 0, new List<DriveSearchResult>());
            }

            throw new NoValidIndexException(driveId);
        }

        private async Task<PagedResult<DriveSearchResult>> CreateSearchResult(Guid driveId, PagedResult<IndexedItem> page, bool includePayload)
        {
            var results = new List<DriveSearchResult>();

            foreach (var item in page.Results)
            {
                var dsr = FromIndexedItem(item);
                if (includePayload)
                {
                    var file = new DriveFileId()
                    {
                        DriveId = driveId,
                        FileId = dsr.FileId
                    };

                    var (tooLarge, size, bytes) = await _driveService.GetPayloadBytes(file);
                    dsr.PayloadTooLarge = tooLarge;
                    dsr.PayloadSize = size;

                    if (!tooLarge)
                    {
                        dsr.PayloadContent = bytes.ToBase64();
                    }
                }

                results.Add(dsr);
            }

            var newResult = new PagedResult<DriveSearchResult>()
            {
                Request = page.Request,
                TotalPages = page.TotalPages,
                Results = results
            };

            return newResult;
        }

        private DriveSearchResult FromIndexedItem(IndexedItem item)
        {
            return new DriveSearchResult()
            {
                FileId = item.FileId,
                ContentIsComplete = item.ContentIsComplete,
                PayloadIsEncrypted = item.PayloadIsEncrypted,
                FileType = item.FileType,
                JsonContent = item.JsonContent,
                Tags = item.Tags,
                CreatedTimestamp = item.CreatedTimestamp,
                LastUpdatedTimestamp = item.LastUpdatedTimestamp,
                SenderDotYouId = item.SenderDotYouId,
                AccessControlList = _context.Caller.IsOwner ? item.AccessControlList : null
            };
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
            var x = _context;
            
            manager = new LiteDbDriveQueryManager(drive, logger,_accessor);

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