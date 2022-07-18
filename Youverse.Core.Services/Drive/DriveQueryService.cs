using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        public async Task<QueryModifiedResult> GetRecent(Guid driveId, FileQueryParams qp, QueryModifiedResultOptions options)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (updatedCursor, fileIdList) = await queryManager.GetRecent(_contextAccessor.GetCurrent().Caller, qp, options);
                var searchResults = await CreateSearchResult(driveId, fileIdList, options);

                //TODO: can we put a stop cursor and update time on this too?  does that make any sense? probably not
                return new QueryModifiedResult()
                {
                    IncludeMetadataHeader = options.IncludeMetadataHeader,
                    Cursor = updatedCursor,
                    SearchResults = searchResults
                };
            }

            throw new NoValidIndexException(driveId);
        }

        public async Task<QueryBatchResult> GetBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (cursor, fileIdList) = await queryManager.GetBatch(_contextAccessor.GetCurrent().Caller, qp, options);
                var searchResults = await CreateSearchResult(driveId, fileIdList, options);

                return new QueryBatchResult()
                {
                    IncludeMetadataHeader = options.IncludeMetadataHeader,
                    Cursor = cursor,
                    SearchResults = searchResults
                };
            }

            throw new NoValidIndexException(driveId);
        }

        private async Task<IEnumerable<DriveSearchResult>> CreateSearchResultOld(Guid driveId, IEnumerable<Guid> fileIdList, ResultOptions options)
        {
            var results = new List<DriveSearchResult>();

            foreach (var fileId in fileIdList)
            {
                var file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = fileId
                };

                //Note: this will fail if the index returns a file the caller cannot access.
                //this can occur when the index is out of sync w/ the header that's on disk
                //this failure is a good thing
                var header = await _driveService.GetServerFileHeader(file);
                var dsr = FromFileMetadata(header);

                //HACK: waiting for indexer to be updated to include payload is encrypted flag
                if (header.FileMetadata.PayloadIsEncrypted && _contextAccessor.GetCurrent().Caller.SecurityLevel == SecurityGroupType.Anonymous)
                {
                    //HACK: skip this file in the search results since anonymous users cannot decrypt files
                    continue;
                }

                //payload is not encrypted, and the caller is anonymous.  which key to use?


                var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(file.DriveId);
                KeyHeader keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

                var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
                var sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, header.EncryptedKeyHeader.Iv, ref clientSharedSecret);
                dsr.SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader;

                if (!options.IncludeMetadataHeader)
                {
                    dsr.JsonContent = "";
                }

                results.Add(dsr);
            }

            return results;
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

                var header = await _appService.GetClientEncryptedFileHeader(file);

                //HACK: waiting for indexer to be updated to include payload is encrypted flag
                if (header.FileMetadata.PayloadIsEncrypted && _contextAccessor.GetCurrent().Caller.SecurityLevel == SecurityGroupType.Anonymous)
                {
                    //HACK: skip this file in the search results since anonymous users cannot decrypt files
                    continue;
                }

                //Note: this will fail if the index returns a file the caller cannot access.
                //this can occur when the index is out of sync w/ the header that's on disk
                //this failure is a good thing
                // var header = await _driveService.GetServerFileHeader(file);
                var dsr = FromFileMetadata(header);

                if (!options.IncludeMetadataHeader)
                {
                    dsr.JsonContent = "";
                }

                results.Add(dsr);
            }

            return results;
        }

        private DriveSearchResult FromFileMetadata(ClientFileHeader header)
        {
            int priority = 1000;


            var metadata = header.FileMetadata;

            //TODO: add other priority based details of SecurityGroupType.CircleConnected and SecurityGroupType.CustomList
            return new DriveSearchResult()
            {
                ContentType = metadata.ContentType,
                FileId = metadata.File.FileId,
                ContentIsComplete = metadata.AppData.ContentIsComplete,
                PayloadIsEncrypted = metadata.PayloadIsEncrypted,
                ThreadId = metadata.AppData.ThreadId,
                FileType = metadata.AppData.FileType,
                DataType = metadata.AppData.DataType,
                UserDate = metadata.AppData.UserDate,
                JsonContent = metadata.AppData.JsonContent,
                Tags = metadata.AppData.Tags,
                CreatedTimestamp = metadata.Created,
                LastUpdatedTimestamp = metadata.Updated,
                SenderDotYouId = metadata.SenderDotYouId,
                AccessControlList = header.ServerMetadata?.AccessControlList,
                Priority = priority
            };
        }


        private DriveSearchResult FromFileMetadata(ServerFileHeader header)
        {
            int priority = 1000;

            switch (header.ServerMetadata.AccessControlList.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    priority = 500;
                    break;
                case SecurityGroupType.Authenticated:
                    priority = 400;
                    break;
                case SecurityGroupType.Connected:
                    priority = 300;
                    break;
                case SecurityGroupType.Owner:
                    priority = 1;
                    break;
            }

            var metadata = header.FileMetadata;

            //TODO: add other priority based details of SecurityGroupType.CircleConnected and SecurityGroupType.CustomList
            return new DriveSearchResult()
            {
                ContentType = metadata.ContentType,
                FileId = metadata.File.FileId,
                ContentIsComplete = metadata.AppData.ContentIsComplete,
                PayloadIsEncrypted = metadata.PayloadIsEncrypted,
                ThreadId = metadata.AppData.ThreadId,
                FileType = metadata.AppData.FileType,
                DataType = metadata.AppData.DataType,
                UserDate = metadata.AppData.UserDate,
                JsonContent = metadata.AppData.JsonContent,
                Tags = metadata.AppData.Tags,
                CreatedTimestamp = metadata.Created,
                LastUpdatedTimestamp = metadata.Updated,
                SenderDotYouId = metadata.SenderDotYouId,
                AccessControlList = _contextAccessor.GetCurrent().Caller.IsOwner ? header.ServerMetadata.AccessControlList : null,
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