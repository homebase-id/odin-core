using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.Sqlite;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    public class DriveQueryService : IDriveQueryService, INotificationHandler<DriveFileChangedNotification>,
        INotificationHandler<DriveFileDeletedNotification>
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly ConcurrentDictionary<Guid, IDriveQueryManager> _queryManagers;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IAppService _appService;

        public DriveQueryService(IDriveService driveService, ILoggerFactory loggerFactory, DotYouContextAccessor contextAccessor, IAppService appService)
        {
            _driveService = driveService;
            _loggerFactory = loggerFactory;
            _contextAccessor = contextAccessor;
            _appService = appService;
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
            foreach (ServerFileHeader header in metaDataList)
            {
                //intentionally letting this run w/o await
                manager.UpdateSecondaryIndex(header);
            }

            await manager.SwitchIndex();
        }

        public async Task<QueryModifiedResult> GetModified(Guid driveId, FileQueryParams qp, QueryModifiedResultOptions options)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (updatedCursor, fileIdList) = await queryManager.GetModified(_contextAccessor.GetCurrent().Caller, qp, options);
                var headers = await CreateClientFileHeaders(driveId, fileIdList, options);

                //TODO: can we put a stop cursor and update time on this too?  does that make any sense? probably not
                return new QueryModifiedResult()
                {
                    IncludesJsonContent = options.IncludeJsonContent,
                    Cursor = updatedCursor,
                    SearchResults = headers
                };
            }

            throw new NoValidIndexException(driveId);
        }

        public async Task<QueryBatchResult> GetBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (cursor, fileIdList) = await queryManager.GetBatch(_contextAccessor.GetCurrent().Caller, qp, options);

                var headers = await CreateClientFileHeaders(driveId, fileIdList, options);
                return new QueryBatchResult()
                {
                    IncludeMetadataHeader = options.IncludeJsonContent,
                    Cursor = cursor,
                    SearchResults = headers
                };
            }

            throw new NoValidIndexException(driveId);
        }
        
        private async Task<IEnumerable<ClientFileHeader>> CreateClientFileHeaders(Guid driveId, IEnumerable<Guid> fileIdList, ResultOptions options)
        {
            var results = new List<ClientFileHeader>();

            foreach (var fileId in fileIdList)
            {
                var file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = fileId
                };

                var header = await _appService.GetClientEncryptedFileHeader(file);
                if (!options.IncludeJsonContent)
                {
                    header.FileMetadata.AppData.JsonContent = string.Empty;
                }

                if (options.ExcludePreviewThumbnail)
                {
                    header.FileMetadata.AppData.PreviewThumbnail = null;
                }

                results.Add(header);
            }

            return results;
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

            foreach (ServerFileHeader header in metaDataList)
            {
                //intentionally letting this run w/o await
                manager.UpdateCurrentIndex(header);
            }

            manager.IndexReadyState = IndexReadyState.Ready;
        }

        private Task<bool> TryGetOrLoadQueryManager(Guid driveId, out IDriveQueryManager manager,
            bool onlyReadyManagers = true)
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

//            manager = new LiteDbDriveQueryManager(drive, logger, _accessor);
            manager = new SqliteQueryManager(drive, logger);
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
            return manager.UpdateCurrentIndex(notification.FileHeader);
        }

        public Task Handle(DriveFileDeletedNotification notification, CancellationToken cancellationToken)
        {
            this.TryGetOrLoadQueryManager(notification.File.DriveId, out var manager, false);
            return manager.RemoveFromCurrentIndex(notification.File);
        }
    }
}