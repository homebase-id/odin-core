using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;
using Serilog;

namespace Odin.Services.Drives.FileSystem.Base
{
    public abstract class DriveQueryServiceBase(
        IOdinContextAccessor contextAccessor,
        DriveDatabaseHost driveDatabaseHost,
        DriveManager driveManager,
        DriveStorageServiceBase storage)
        : RequirePermissionsBase
    {
        protected override DriveManager DriveManager { get; } = driveManager;

        protected override IOdinContextAccessor ContextAccessor { get; } = contextAccessor;

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> the inheriting class manages
        /// </summary>
        protected abstract FileSystemType GetFileSystemType();

        public async Task<DriveSizeInfo> GetDriveSize(Guid driveId)
        {
            await AssertCanReadOrWriteToDrive(driveId);
            var queryManager = await TryGetOrLoadQueryManager(driveId);
            var (fileCount, bytes) = await queryManager.GetDriveSizeInfo();

            return new DriveSizeInfo()
            {
                FileCount = fileCount,
                Size = bytes
            };
        }

        public async Task<QueryModifiedResult> GetModified(Guid driveId, FileQueryParams qp, QueryModifiedResultOptions options)
        {
            await AssertCanReadDrive(driveId);

            var o = options ?? QueryModifiedResultOptions.Default();

            var queryManager = await TryGetOrLoadQueryManager(driveId);
            if (queryManager != null)
            {
                var (updatedCursor, fileIdList, hasMoreRows) =
                    await queryManager.GetModifiedCore(ContextAccessor.GetCurrent(), GetFileSystemType(), qp, o);
                var headers = await CreateClientFileHeaders(driveId, fileIdList, o);

                //TODO: can we put a stop cursor and update time on this too?  does that make any sense? probably not
                return new QueryModifiedResult()
                {
                    IncludeHeaderContent = o.IncludeHeaderContent,
                    Cursor = updatedCursor,
                    SearchResults = headers,
                    HasMoreRows = hasMoreRows
                };
            }

            throw new NoValidIndexClientException(driveId);
        }

        public async Task<QueryBatchResult> GetBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options, bool forceIncludeServerMetadata = false)
        {
            await AssertCanReadDrive(driveId);
            return await GetBatchInternal(driveId, qp, options, forceIncludeServerMetadata);
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId, bool excludePreviewThumbnail = true)
        {
            await AssertCanReadDrive(driveId);

            var qp = new FileQueryParams()
            {
                ClientUniqueIdAtLeastOne = new List<Guid>() { clientUniqueId }
            };

            var options = new QueryBatchResultOptions()
            {
                Cursor = null,
                MaxRecords = 10,
                IncludeHeaderContent = !excludePreviewThumbnail,
                ExcludePreviewThumbnail = excludePreviewThumbnail
            };

            var results = await this.GetBatch(driveId, qp, options);

            return results.SearchResults.SingleOrDefault();
        }

        public async Task<QueryBatchCollectionResponse> GetBatchCollection(QueryBatchCollectionRequest request, bool forceIncludeServerMetadata = false)
        {
            if (request.Queries.DistinctBy(q => q.Name).Count() != request.Queries.Count())
            {
                throw new OdinClientException("The Names of Queries must be unique", OdinClientErrorCode.InvalidQuery);
            }

            var permissionContext = ContextAccessor.GetCurrent().PermissionsContext;

            var collection = new QueryBatchCollectionResponse();
            foreach (var query in request.Queries)
            {
                var targetDrive = query.QueryParams.TargetDrive;

                var canReadDrive = permissionContext.HasDriveId(targetDrive, out var driveIdValue) &&
                                   permissionContext.HasDrivePermission(driveIdValue.GetValueOrDefault(), DrivePermission.Read);

                if (canReadDrive)
                {
                    var driveId = driveIdValue.GetValueOrDefault();
                    var options = query.ResultOptionsRequest?.ToQueryBatchResultOptions() ?? new QueryBatchResultOptions()
                    {
                        IncludeHeaderContent = true,
                        ExcludePreviewThumbnail = false
                    };

                    var result = await this.GetBatch(driveId, query.QueryParams, options, forceIncludeServerMetadata);

                    var response = QueryBatchResponse.FromResult(result);
                    response.Name = query.Name;
                    collection.Results.Add(response);
                }
                else
                {
                    collection.Results.Add(QueryBatchResponse.FromInvalidDrive(query.Name));
                }
            }

            return collection;
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId, bool forceIncludeServerMetadata = false,
            bool excludePreviewThumbnail = true)
        {
            await AssertCanReadOrWriteToDrive(driveId);
            var qp = new FileQueryParams()
            {
                GlobalTransitId = new List<Guid>() { globalTransitId }
            };

            var options = new QueryBatchResultOptions()
            {
                Cursor = null,
                MaxRecords = 10,
                ExcludePreviewThumbnail = excludePreviewThumbnail
            };

            var results = await this.GetBatchInternal(driveId, qp, options, forceIncludeServerMetadata);

            return results.SearchResults.SingleOrDefault();
        }

        public async Task<InternalDriveFileId?> ResolveFileId(GlobalTransitIdFileIdentifier file)
        {
            var driveId = ContextAccessor.GetCurrent().PermissionsContext.GetDriveId(file.TargetDrive);
            await AssertCanReadOrWriteToDrive(driveId);

            var qp = new FileQueryParams()
            {
                GlobalTransitId = new List<Guid>() { file.GlobalTransitId }
            };

            var options = new QueryBatchResultOptions()
            {
                Cursor = null,
                MaxRecords = 10,
                ExcludePreviewThumbnail = true
            };

            var queryManager = await TryGetOrLoadQueryManager(driveId);
            if (queryManager != null)
            {
                var (_, fileIdList, _) = await queryManager.GetBatchCore(ContextAccessor.GetCurrent(),
                    GetFileSystemType(),
                    qp,
                    options);

                var fileId = fileIdList.FirstOrDefault();

                if (fileId == Guid.Empty)
                {
                    return null;
                }

                return new InternalDriveFileId()
                {
                    FileId = fileId,
                    DriveId = driveId
                };
            }

            throw new NoValidIndexClientException(driveId);
        }

        private async Task<IEnumerable<SharedSecretEncryptedFileHeader>> CreateClientFileHeaders(Guid driveId,
            IEnumerable<Guid> fileIdList, ResultOptions options, bool forceIncludeServerMetadata = false)
        {
            var results = new List<SharedSecretEncryptedFileHeader>();

            foreach (var fileId in fileIdList)
            {
                var file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = fileId
                };

                var hasPermissionToFile = await storage.CallerHasPermissionToFile(file);
                if (!hasPermissionToFile)
                {
                    // throw new OdinSystemException($"Caller with OdinId [{ContextAccessor.GetCurrent().Caller.OdinId}] received the file from the drive" +
                    //                               $" search index but does not have read access to the file:{file.FileId} on drive:{file.DriveId}");
                    Log.Error($"Caller with OdinId [{ContextAccessor.GetCurrent().Caller.OdinId}] received the file from the drive" +
                              $" search index but does not have read access to the file:{file.FileId} on drive:{file.DriveId}");
                }
                else
                {
                    var serverFileHeader = await storage.GetServerFileHeader(file);
                    var isEncrypted = serverFileHeader.FileMetadata.IsEncrypted;
                    var hasStorageKey = ContextAccessor.GetCurrent().PermissionsContext.TryGetDriveStorageKey(file.DriveId, out var _);

                    //Note: it is possible that an app can have read access to a drive that allows anonymous but not have the storage key   
                    var shouldReceiveFile = (isEncrypted && hasStorageKey) || !isEncrypted;
                    if (shouldReceiveFile)
                    {
                        var header = DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(
                            serverFileHeader,
                            ContextAccessor,
                            forceIncludeServerMetadata);
                        if (!options.IncludeHeaderContent)
                        {
                            header.FileMetadata.AppData.Content = string.Empty;
                        }

                        if (options.ExcludePreviewThumbnail)
                        {
                            header.FileMetadata.AppData.PreviewThumbnail = null;
                            foreach (var pd in header.FileMetadata.Payloads)
                            {
                                pd.PreviewThumbnail = null;
                            }
                        }

                        if (options.ExcludeServerMetaData)
                        {
                            header.ServerMetadata = null;
                        }

                        results.Add(header);
                    }
                    else
                    {
                        var drive = await DriveManager.GetDrive(file.DriveId);
                        Log.Debug("Caller with OdinId [{odinid}] received the file from the drive search " +
                                  "index with (isPayloadEncrypted: {isencrypted} and auth context[{authContext}]) but does not have the " +
                                  "storage key to decrypt the file {file} on drive ({driveName}, allow anonymous: {driveAllowAnon}) " +
                                  "[alias={driveAlias}, type={driveType}]",
                            ContextAccessor.GetCurrent().Caller.OdinId,
                            serverFileHeader.FileMetadata.IsEncrypted,
                            ContextAccessor.GetCurrent().AuthContext,
                            file.FileId,
                            drive.Name,
                            drive.AllowAnonymousReads,
                            drive.TargetDriveInfo.Alias.Value.ToString(),
                            drive.TargetDriveInfo.Type.Value.ToString());
                    }
                }
            }

            return results;
        }

        private async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId)
        {
            return await driveDatabaseHost.TryGetOrLoadQueryManager(driveId);
        }

        /// <summary>
        /// Gets the file Id a file by its <see cref="GlobalTransitIdFileIdentifier"/>
        /// </summary>
        /// <returns>The fileId; otherwise null if the file does not exist</returns>
        private async Task<QueryBatchResult> GetBatchInternal(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options,
            bool forceIncludeServerMetadata = false)
        {
            var queryManager = await TryGetOrLoadQueryManager(driveId);
            if (queryManager != null)
            {
                var queryTime = UnixTimeUtcUnique.Now();
                var (cursor, fileIdList, hasMoreRows) = await queryManager.GetBatchCore(ContextAccessor.GetCurrent(),
                    GetFileSystemType(),
                    qp,
                    options);


                var headers = await CreateClientFileHeaders(driveId, fileIdList, options, forceIncludeServerMetadata);
                return new QueryBatchResult()
                {
                    QueryTime = queryTime.uniqueTime,
                    IncludeMetadataHeader = options.IncludeHeaderContent,
                    Cursor = cursor,
                    SearchResults = headers,
                    HasMoreRows = hasMoreRows
                };
            }

            throw new NoValidIndexClientException(driveId);
        }
    }
}