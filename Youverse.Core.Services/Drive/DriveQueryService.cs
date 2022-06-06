using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.Sqlite;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Drive
{
    public class DriveQueryService : IDriveQueryService, INotificationHandler<DriveFileChangedNotification>,
        INotificationHandler<DriveFileDeletedNotification>
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly ConcurrentDictionary<Guid, IDriveQueryManager> _queryManagers;
        private readonly ILoggerFactory _loggerFactory;

        public DriveQueryService(IDriveService driveService, ILoggerFactory loggerFactory, DotYouContextAccessor contextAccessor)
        {
            _driveService = driveService;
            _loggerFactory = loggerFactory;
            _contextAccessor = contextAccessor;
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

        public async Task<QueryBatchResult> GetRecent(Guid driveId, ulong maxDate, byte[] startCursor, QueryParams qp, ResultOptions options)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (cursor, fileIdList) = await queryManager.GetRecent(maxDate, startCursor, qp, options);
                var searchResults = await CreateSearchResult(driveId, fileIdList, options);

                //TODO: can we put a stop cursor and udpate time on this too?  does that make any sense? probably not
                return new QueryBatchResult()
                {
                    StartCursor = cursor,
                    StopCursor = null,
                    CursorUpdatedTimestamp = 0,
                    SearchResults = searchResults
                };
            }

            throw new NoValidIndexException(driveId);
        }

        public async Task<QueryBatchResult> GetBatch(Guid driveId, byte[] startCursor, byte[] stopCursor, QueryParams qp, ResultOptions options)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (resultStartCursor, resultStopCursor, cursorUpdatedTimestamp, fileIdList) = await queryManager.GetBatch(startCursor, stopCursor, qp, options);
                var searchResults = await CreateSearchResult(driveId, fileIdList, options);

                return new QueryBatchResult()
                {
                    StartCursor = resultStartCursor,
                    StopCursor = resultStopCursor,
                    CursorUpdatedTimestamp = cursorUpdatedTimestamp,
                    SearchResults = searchResults
                };
            }

            throw new NoValidIndexException(driveId);
        }

        private async Task<IEnumerable<DriveSearchResult>> CreateSearchResult(Guid driveId, IEnumerable<Guid> fileIdList, ResultOptions options)
        {
            var results = new List<DriveSearchResult>();

            foreach (var fileId in fileIdList)
            {
                var file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = fileId
                };

                var (md, acl) = await _driveService.GetMetadata(file);
                var dsr = FromFileMetadata(md, acl);

                if (!options.IncludeMetadataHeader)
                {
                    dsr.JsonContent = "";
                }

                if (options.IncludePayload)
                {
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

            return results;
        }

        private DriveSearchResult FromFileMetadata(FileMetadata metadata, AccessControlList acl)
        {
            int priority = 1000;

            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    priority = 500;
                    break;
                case SecurityGroupType.YouAuthOrTransitCertificateIdentified:
                    priority = 400;
                    break;
                case SecurityGroupType.Connected:
                    priority = 300;
                    break;
                case SecurityGroupType.CircleConnected:
                    priority = 200;
                    break;
                case SecurityGroupType.CustomList:
                    priority = 100;
                    break;
                case SecurityGroupType.Owner:
                    priority = 1;
                    break;
            }

            //TODO: add other priority based details of SecurityGroupType.CircleConnected and SecurityGroupType.CustomList
            return new DriveSearchResult()
            {
                ContentType = metadata.ContentType,
                FileId = metadata.File.FileId,
                ContentIsComplete = metadata.AppData.ContentIsComplete,
                PayloadIsEncrypted = metadata.PayloadIsEncrypted,
                FileType = metadata.AppData.FileType,
                DataType = metadata.AppData.DataType,
                JsonContent = metadata.AppData.JsonContent,
                Tags = metadata.AppData.Tags,
                CreatedTimestamp = metadata.Created,
                LastUpdatedTimestamp = metadata.Updated,
                SenderDotYouId = metadata.SenderDotYouId,
                AccessControlList = _contextAccessor.GetCurrent().Caller.IsOwner ? metadata.AccessControlList : null,
                Priority = priority
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
            return manager.UpdateCurrentIndex(notification.FileMetadata);
        }

        public Task Handle(DriveFileDeletedNotification notification, CancellationToken cancellationToken)
        {
            this.TryGetOrLoadQueryManager(notification.File.DriveId, out var manager, false);
            return manager.RemoveFromCurrentIndex(notification.File);
        }
    }
}