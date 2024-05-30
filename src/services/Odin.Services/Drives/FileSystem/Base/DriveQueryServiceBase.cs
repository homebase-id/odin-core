using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Base
{
    public abstract class DriveQueryServiceBase : RequirePermissionsBase
    {
        private readonly ILogger _logger;
        private readonly DriveStorageServiceBase _storage;
        private readonly DriveDatabaseHost _driveDatabaseHost;

        protected DriveQueryServiceBase(
            ILogger logger,
            DriveDatabaseHost driveDatabaseHost,
            DriveManager driveManager,
            DriveStorageServiceBase storage)
        {
            _logger = logger;
            DriveManager = driveManager;
            _driveDatabaseHost = driveDatabaseHost;
            _storage = storage;
        }

        protected override DriveManager DriveManager { get; }

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> the inheriting class manages
        /// </summary>
        protected abstract FileSystemType GetFileSystemType();

        public async Task<DriveSizeInfo> GetDriveSize(Guid driveId, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanReadOrWriteToDrive(driveId, odinContext, cn);
            var queryManager = await TryGetOrLoadQueryManager(driveId, cn);
            var (fileCount, bytes) = await queryManager.GetDriveSizeInfo(cn);

            return new DriveSizeInfo()
            {
                FileCount = fileCount,
                Size = bytes
            };
        }

        public async Task<QueryModifiedResult> GetModified(Guid driveId, FileQueryParams qp, QueryModifiedResultOptions options, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            await AssertCanReadDrive(driveId, odinContext, cn);

            var o = options ?? QueryModifiedResultOptions.Default();

            var queryManager = await TryGetOrLoadQueryManager(driveId, cn);
            if (queryManager != null)
            {
                var (updatedCursor, fileIdList, hasMoreRows) =
                    await queryManager.GetModifiedCore(odinContext, GetFileSystemType(), qp, o, cn);
                var headers = await CreateClientFileHeaders(driveId, fileIdList, o, odinContext, cn);

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

        public async Task<QueryBatchResult> GetBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options, IOdinContext odinContext,
            DatabaseConnection cn,
            bool forceIncludeServerMetadata = false)
        {
            await AssertCanReadDrive(driveId, odinContext, cn);
            return await GetBatchInternal(driveId, qp, options, odinContext, cn, forceIncludeServerMetadata);
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId, IOdinContext odinContext,
            DatabaseConnection cn,
            bool excludePreviewThumbnail = true)
        {
            // await AssertCanReadDrive(driveId, odinContext, cn);
            await AssertCanReadOrWriteToDrive(driveId, odinContext, cn);

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

            var results = await this.GetBatchInternal(driveId, qp, options, odinContext, cn);

            return results.SearchResults.SingleOrDefault();
        }

        public async Task<QueryBatchCollectionResponse> GetBatchCollection(QueryBatchCollectionRequest request, IOdinContext odinContext, DatabaseConnection cn,
            bool forceIncludeServerMetadata = false)
        {
            if (request.Queries.DistinctBy(q => q.Name).Count() != request.Queries.Count())
            {
                throw new OdinClientException("The Names of Queries must be unique", OdinClientErrorCode.InvalidQuery);
            }

            var permissionContext = odinContext.PermissionsContext;

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

                    var result = await this.GetBatch(driveId, query.QueryParams, options, odinContext, cn, forceIncludeServerMetadata);

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

        public async Task<QueryBatchCollectionResponse> DumpGlobalTransitId(List<StorageDrive> drives, Guid uniqueId, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var request = new QueryBatchCollectionRequest
            {
                Queries = drives.Select(drive => new CollectionQueryParamSection()
                {
                    Name = $"DriveName-{drive.Name}-x'{Convert.ToHexString(drive.Id.ToByteArray())}'",
                    QueryParams = new FileQueryParams
                    {
                        TargetDrive = drive.TargetDriveInfo,
                        GlobalTransitId = [uniqueId],
                        FileState = [FileState.Active, FileState.Deleted]
                    }
                }).ToList()
            };

            var collection = new QueryBatchCollectionResponse();
            foreach (var query in request.Queries)
            {
                var drive = drives.SingleOrDefault(d => d.TargetDriveInfo == query.QueryParams.TargetDrive);
                var options = query.ResultOptionsRequest?.ToQueryBatchResultOptions() ?? new QueryBatchResultOptions()
                {
                    IncludeHeaderContent = true,
                    ExcludePreviewThumbnail = false
                };

                var result = await this.GetBatchInternal(drive!.Id, query.QueryParams, options, odinContext, cn, true);

                var response = QueryBatchResponse.FromResult(result);
                response.Name = query.Name;
                collection.Results.Add(response);
            }

            return collection;
        }

        public async Task<UniqueIdDump> DumpUniqueId(List<StorageDrive> drives, Guid uniqueId, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var request = new QueryBatchCollectionRequest
            {
                Queries = drives.Select(drive => new CollectionQueryParamSection()
                {
                    Name = $"DriveName-{drive.Name}-x'{Convert.ToHexString(drive.Id.ToByteArray())}'",
                    QueryParams = new FileQueryParams
                    {
                        TargetDrive = drive.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = [uniqueId],
                        FileState = [FileState.Active, FileState.Deleted]
                    }
                }).ToList()
            };

            var d = new UniqueIdDump();
            foreach (var query in request.Queries)
            {
                var drive = drives.SingleOrDefault(d => d.TargetDriveInfo == query.QueryParams.TargetDrive);
                var options = query.ResultOptionsRequest?.ToQueryBatchResultOptions() ?? new QueryBatchResultOptions()
                {
                    IncludeHeaderContent = true,
                    ExcludePreviewThumbnail = false
                };

                var queryManager = await TryGetOrLoadQueryManager(drive!.Id, cn);
                var (cursor, fileIdList, hasMoreRows) = await queryManager.GetBatchCore(odinContext,
                    GetFileSystemType(),
                    query.QueryParams,
                    options,
                    cn);

                d.Results.Add(new DumpResult()
                {
                    Name = query.Name,
                    FileIdList = fileIdList.ToList()
                });
            }

            return d;
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId, IOdinContext odinContext,
            DatabaseConnection cn,
            bool forceIncludeServerMetadata = false,
            bool excludePreviewThumbnail = true)
        {
            await AssertCanReadOrWriteToDrive(driveId, odinContext, cn);
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

            var results = await this.GetBatchInternal(driveId, qp, options, odinContext, cn, forceIncludeServerMetadata);

            return results.SearchResults.SingleOrDefault();
        }

        public async Task<InternalDriveFileId?> ResolveFileId(GlobalTransitIdFileIdentifier file, IOdinContext odinContext, DatabaseConnection cn)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(file.TargetDrive);
            await AssertCanReadOrWriteToDrive(driveId, odinContext, cn);

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

            var queryManager = await TryGetOrLoadQueryManager(driveId, cn);
            if (queryManager != null)
            {
                var (_, fileIdList, _) = await queryManager.GetBatchCore(odinContext,
                    GetFileSystemType(),
                    qp,
                    options,
                    cn);

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
            IEnumerable<Guid> fileIdList, ResultOptions options, IOdinContext odinContext, DatabaseConnection cn, bool forceIncludeServerMetadata = false)
        {
            var results = new List<SharedSecretEncryptedFileHeader>();

            foreach (var fileId in fileIdList)
            {
                var file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = fileId
                };

                var hasPermissionToFile = await _storage.CallerHasPermissionToFile(file, odinContext, cn);
                if (!hasPermissionToFile)
                {
                    // throw new OdinSystemException($"Caller with OdinId [{odinContext.Caller.OdinId}] received the file from the drive" +
                    //                               $" search index but does not have read access to the file:{file.FileId} on drive:{file.DriveId}");
                    _logger.LogError($"Caller with OdinId [{odinContext.Caller.OdinId}] received the file from the drive" +
                                     $" search index but does not have read access to the file:{file.FileId} on drive:{file.DriveId}");
                }
                else
                {
                    var serverFileHeader = await _storage.GetServerFileHeader(file, odinContext, cn);
                    var isEncrypted = serverFileHeader.FileMetadata.IsEncrypted;
                    var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(file.DriveId, out _);

                    //Note: it is possible that an app can have read access to a drive that allows anonymous but not have the storage key   
                    var shouldReceiveFile = (isEncrypted && hasStorageKey) || !isEncrypted;
                    if (shouldReceiveFile)
                    {
                        var header = DriveFileUtility.CreateClientFileHeader(
                            serverFileHeader,
                            odinContext,
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
                        else
                        {
                            if (!options.IncludeTransferHistory && header.ServerMetadata != null)
                            {
                                header.ServerMetadata.TransferHistory = null;
                            }
                        }

                        results.Add(header);
                    }
                    else
                    {
                        var drive = await DriveManager.GetDrive(file.DriveId, cn);
                        _logger.LogDebug("Caller with OdinId [{odinid}] received the file from the drive search " +
                                         "index with (isPayloadEncrypted: {isencrypted} and auth context[{authContext}]) but does not have the " +
                                         "storage key to decrypt the file {file} on drive ({driveName}, allow anonymous: {driveAllowAnon}) " +
                                         "[alias={driveAlias}, type={driveType}]",
                            odinContext.Caller.OdinId,
                            serverFileHeader.FileMetadata.IsEncrypted,
                            odinContext.AuthContext,
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

        private async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId, DatabaseConnection cn)
        {
            return await _driveDatabaseHost.TryGetOrLoadQueryManager(driveId, cn);
        }

        /// <summary>
        /// Gets the file Id a file by its <see cref="GlobalTransitIdFileIdentifier"/>
        /// </summary>
        /// <returns>The fileId; otherwise null if the file does not exist</returns>
        private async Task<QueryBatchResult> GetBatchInternal(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options, IOdinContext odinContext,
            DatabaseConnection cn, bool forceIncludeServerMetadata = false)
        {
            var queryManager = await TryGetOrLoadQueryManager(driveId, cn);
            if (queryManager != null)
            {
                var queryTime = UnixTimeUtcUnique.Now();
                var (cursor, fileIdList, hasMoreRows) = await queryManager.GetBatchCore(odinContext,
                    GetFileSystemType(),
                    qp,
                    options,
                    cn);


                var headers = await CreateClientFileHeaders(driveId, fileIdList, options, odinContext, cn, forceIncludeServerMetadata);
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

    public class UniqueIdDump
    {
        public List<DumpResult> Results { get; set; } = new List<DumpResult>();
    }

    public class DumpResult
    {
        public List<Guid> FileIdList { get; set; }
        public string Name { get; set; }
    }
}