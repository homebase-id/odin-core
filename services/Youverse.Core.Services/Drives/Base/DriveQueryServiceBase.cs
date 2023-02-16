using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drives.FileSystem;

namespace Youverse.Core.Services.Drives.Base
{
    public abstract class DriveQueryServiceBase : RequirePermissionsBase
    {
        private readonly DriveStorageServiceBase _storage;
        private readonly DriveDatabaseHost _driveDatabaseHost;

        protected DriveQueryServiceBase(DotYouContextAccessor contextAccessor, DriveDatabaseHost driveDatabaseHost, DriveManager driveManager, DriveStorageServiceBase storage)
        {
            ContextAccessor = contextAccessor;
            DriveManager = driveManager;
            _driveDatabaseHost = driveDatabaseHost;
            _storage = storage;
        }

        protected override DriveManager DriveManager { get; }

        protected override DotYouContextAccessor ContextAccessor { get; }

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> the inheriting class manages
        /// </summary>
        protected abstract FileSystemType GetFileSystemType();

        public async Task<QueryModifiedResult> GetModified(Guid driveId, FileQueryParams qp, QueryModifiedResultOptions options)
        {
            AssertCanReadDrive(driveId);
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (updatedCursor, fileIdList) = await queryManager.GetModified(ContextAccessor.GetCurrent().Caller, GetFileSystemType(), qp, options);
                var headers = await CreateClientFileHeaders(driveId, fileIdList, options);

                //TODO: can we put a stop cursor and update time on this too?  does that make any sense? probably not
                return new QueryModifiedResult()
                {
                    IncludesJsonContent = options.IncludeJsonContent,
                    Cursor = updatedCursor,
                    SearchResults = headers
                };
            }

            throw new NoValidIndexClientException(driveId);
        }

        public async Task<QueryBatchResult> GetBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options)
        {
            AssertCanReadDrive(driveId);

            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                var (cursor, fileIdList) = await queryManager.GetBatch(ContextAccessor.GetCurrent().Caller,
                    GetFileSystemType(),
                    qp,
                    options);

                var headers = await CreateClientFileHeaders(driveId, fileIdList, options);
                return new QueryBatchResult()
                {
                    IncludeMetadataHeader = options.IncludeJsonContent,
                    Cursor = cursor,
                    SearchResults = headers
                };
            }

            throw new NoValidIndexClientException(driveId);
        }

        public async Task<ClientFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId)
        {
            AssertCanReadDrive(driveId);

            var qp = new FileQueryParams()
            {
                ClientUniqueIdAtLeastOne = new List<Guid>() { clientUniqueId }
            };

            var options = new QueryBatchResultOptions()
            {
                Cursor = null,
                MaxRecords = 10,
                ExcludePreviewThumbnail = true
            };

            var results = await this.GetBatch(driveId, qp, options);

            return results.SearchResults.SingleOrDefault();
        }

        public async Task<QueryBatchCollectionResponse> GetBatchCollection(QueryBatchCollectionRequest request)
        {
            foreach (var driveId in request.Queries.Select(q => ContextAccessor.GetCurrent().PermissionsContext.GetDriveId(q.QueryParams.TargetDrive)))
            {
                AssertCanReadDrive(driveId);
            }

            var collection = new QueryBatchCollectionResponse();
            foreach (var query in request.Queries)
            {
                var driveId = (await DriveManager.GetDriveIdByAlias(query.QueryParams.TargetDrive, true)).GetValueOrDefault();
                var result = await this.GetBatch(driveId, query.QueryParams, query.ResultOptions);

                var response = QueryBatchResponse.FromResult(result);
                response.Name = query.Name;
                collection.Results.Add(response);
            }

            return collection;
        }

        public Task EnsureIndexerCommits(IEnumerable<Guid> driveIdList)
        {
            foreach (var driveId in driveIdList)
            {
                AssertCanWriteToDrive(driveId);
                if (this.TryGetOrLoadQueryManager(driveId, out var manager, false).GetAwaiter().GetResult())
                {
                    manager.EnsureIndexDataCommitted();
                }
            }

            return Task.CompletedTask;
        }

        public async Task<ClientFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId)
        {
            AssertCanReadDrive(driveId);
            var qp = new FileQueryParams()
            {
                GlobalTransitId = new List<Guid>() { globalTransitId }
            };

            var options = new QueryBatchResultOptions()
            {
                Cursor = null,
                MaxRecords = 10,
                ExcludePreviewThumbnail = true
            };

            var results = await this.GetBatch(driveId, qp, options);

            return results.SearchResults.SingleOrDefault();
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

                var serverFileHeader = await _storage.GetServerFileHeader(file);
                var header = Utility.ConvertToSharedSecretEncryptedClientFileHeader(serverFileHeader, ContextAccessor);
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

        private Task<bool> TryGetOrLoadQueryManager(Guid driveId, out IDriveQueryManager manager, bool onlyReadyManagers = true)
        {
            return _driveDatabaseHost.TryGetOrLoadQueryManager(driveId, out manager, onlyReadyManagers);
        }
    }
}