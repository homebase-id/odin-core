using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
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
        private readonly DriveQuery _driveQuery;
        private readonly DriveStorageServiceBase _storage;
        private readonly IdentityDatabase _db;

        protected DriveQueryServiceBase(
            ILogger logger,
            DriveManager driveManager,
            DriveQuery driveQuery,
            DriveStorageServiceBase storage,
            IdentityDatabase db
        )
        {
            _logger = logger;
            _driveQuery = driveQuery;
            DriveManager = driveManager;
            _storage = storage;
            _db = db;
        }

        protected override DriveManager DriveManager { get; }

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> the inheriting class manages
        /// </summary>
        protected abstract FileSystemType GetFileSystemType();

        public async Task<DriveSizeInfo> GetDriveSize(Guid driveId, IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var drive = await DriveManager.GetDriveAsync(driveId, failIfInvalid: true);
            var (fileCount, bytes) = await _driveQuery.GetDriveSizeInfoAsync(drive);

            return new DriveSizeInfo()
            {
                FileCount = fileCount,
                Size = bytes
            };
        }

        public async Task<QueryModifiedResult> GetModified(Guid driveId, FileQueryParams qp, QueryModifiedResultOptions options,
            IOdinContext odinContext)
        {
            await AssertCanReadDriveAsync(driveId, odinContext);

            var o = options ?? QueryModifiedResultOptions.Default();

            var drive = await DriveManager.GetDriveAsync(driveId, failIfInvalid: true);
            var (updatedCursor, recordList, hasMoreRows) =
                await _driveQuery.GetModifiedCoreAsync(drive, odinContext, GetFileSystemType(), qp, o);

            var headers = await CreateClientFileHeadersAsync(driveId, recordList, o, odinContext);

            //TODO: can we put a stop cursor and update time on this too?  does that make any sense? probably not
            return new QueryModifiedResult()
            {
                IncludeHeaderContent = o.IncludeHeaderContent,
                Cursor = updatedCursor,
                SearchResults = headers,
                HasMoreRows = hasMoreRows
            };
        }

        public async Task<QueryBatchResult> GetBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false)
        {
            await AssertCanReadDriveAsync(driveId, odinContext);
            return await GetBatchInternal(driveId, qp, options, odinContext, forceIncludeServerMetadata);
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId,
            ResultOptions options, IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var record = await _driveQuery.GetByClientUniqueIdAsync(driveId, clientUniqueId, GetFileSystemType());

            if (null == record)
            {
                return null;
            }

            var headers = await CreateClientFileHeadersAsync(driveId, [record], options, odinContext);
            return headers.SingleOrDefault();
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId,
            IOdinContext odinContext,
            bool excludePreviewThumbnail = true)
        {
            var options = new ResultOptions()
            {
                MaxRecords = 10,
                IncludeHeaderContent = !excludePreviewThumbnail,
                ExcludePreviewThumbnail = excludePreviewThumbnail
            };

            return await GetFileByClientUniqueId(driveId, clientUniqueId, options, odinContext);
        }

        public async Task<QueryBatchCollectionResponse> GetBatchCollection(QueryBatchCollectionRequest request, IOdinContext odinContext,
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

                    var result = await this.GetBatch(driveId, query.QueryParams, options, odinContext, forceIncludeServerMetadata);

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

        public async Task<QueryBatchCollectionResponse> DumpGlobalTransitId(List<StorageDrive> drives, Guid uniqueId,
            IOdinContext odinContext)
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

                var result = await this.GetBatchInternal(drive!.Id, query.QueryParams, options, odinContext, true);

                var response = QueryBatchResponse.FromResult(result);
                response.Name = query.Name;
                collection.Results.Add(response);
            }

            return collection;
        }

        public async Task<UniqueIdDump> DumpUniqueId(List<StorageDrive> drives, Guid uniqueId, IOdinContext odinContext)
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
                var drive = drives.SingleOrDefault(storageDrive => storageDrive.TargetDriveInfo == query.QueryParams.TargetDrive);
                var options = query.ResultOptionsRequest?.ToQueryBatchResultOptions() ?? new QueryBatchResultOptions()
                {
                    IncludeHeaderContent = true,
                    ExcludePreviewThumbnail = false
                };

                var (_, records, _) = await _driveQuery.GetBatchCoreAsync(
                    drive,
                    odinContext,
                    GetFileSystemType(),
                    query.QueryParams,
                    options);

                d.Results.Add(new DumpResult()
                {
                    Name = query.Name,
                    RecordList = records
                });
            }

            return d;
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false,
            bool excludePreviewThumbnail = true,
            bool includeTransferHistory = true)
        {
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var record = await _driveQuery.GetByGlobalTransitIdAsync(driveId, globalTransitId, GetFileSystemType());

            if (null == record)
            {
                _logger.LogDebug("GetFileByGlobalTransitId returned null for gtid {globalTransitId}", globalTransitId);
                return null;
            }

            _logger.LogDebug("GetFileByGlobalTransitId returned fileId {fileId} for gtid {globalTransitId}", record.fileId,
                globalTransitId);

            var options = new ResultOptions()
            {
                MaxRecords = 10,
                ExcludePreviewThumbnail = excludePreviewThumbnail,
                IncludeHeaderContent = true,
                IncludeTransferHistory = includeTransferHistory
            };

            var headers = await CreateClientFileHeadersAsync(driveId, [record], options, odinContext, forceIncludeServerMetadata);

            _logger.LogDebug("GetFileByGlobalTransitId ");

            return headers.SingleOrDefault();
        }

        public async Task<InternalDriveFileId?> ResolveFileId(GlobalTransitIdFileIdentifier file, IOdinContext odinContext)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(file.TargetDrive);
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var record = await _driveQuery.GetByGlobalTransitIdAsync(driveId, file.GlobalTransitId, GetFileSystemType());

            if (null == record)
            {
                return null;
            }

            return new InternalDriveFileId()
            {
                FileId = record.fileId,
                DriveId = driveId
            };
        }

        private async Task<IEnumerable<SharedSecretEncryptedFileHeader>> CreateClientFileHeadersAsync(Guid driveId,
            List<DriveMainIndexRecord> recordList, ResultOptions options, IOdinContext odinContext,
            bool forceIncludeServerMetadata = false)
        {
            var results = new List<SharedSecretEncryptedFileHeader>();

            foreach (var record in recordList)
            {
                var file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = record.fileId
                };

                var serverFileHeader = ServerFileHeader.FromDriveMainIndexRecord(record);
                if (null == serverFileHeader)
                {
                    _logger.LogError("File {file} on drive {drive} was found in index but was not returned from disk", file.FileId,
                        file.DriveId);
                    continue;
                }

                var hasPermissionToFile = await _storage.CallerHasPermissionToFile(serverFileHeader, odinContext);
                if (!hasPermissionToFile)
                {
                    _logger.LogError($"Caller with OdinId [{odinContext.Caller.OdinId}] received the file from the drive" +
                                     $" search index but does not have read access to the file:{file.FileId} on drive:{file.DriveId}");
                }
                else
                {
                    if (serverFileHeader.FileMetadata.FileState == FileState.Deleted)
                    {
                        _logger.LogDebug("Creating Client File Header for deleted file (File {file} on drive {drive})", file.FileId,
                            file.DriveId);
                        // var header = DriveFileUtility.CreateDeletedClientFileHeader(serverFileHeader, odinContext);
                        // results.Add(header);
                        // continue;
                    }

                    var isEncrypted = serverFileHeader.FileMetadata.IsEncrypted;
                    var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(file.DriveId, out _);

                    _logger.LogDebug("Caller [{odinid}] has storage key: {hasStorageKey} ", odinContext.Caller.OdinId,
                        hasStorageKey ? "yes" : "no");

                    //Note: it is possible that an app can have read access to a drive that allows anonymous but not have the storage key   
                    var shouldReceiveFile = (isEncrypted && hasStorageKey) || !isEncrypted;
                    if (shouldReceiveFile)
                    {
                        var header = DriveFileUtility.CreateClientFileHeader(
                            serverFileHeader,
                            odinContext,
                            forceIncludeServerMetadata);

                        if (header?.FileMetadata?.AppData != null)
                        {
                            if (!options.IncludeHeaderContent)
                            {
                                header.FileMetadata.AppData.Content = string.Empty;
                            }


                            if (options.ExcludePreviewThumbnail)
                            {
                                header.FileMetadata.AppData.PreviewThumbnail = null;
                                if (null != header.FileMetadata.Payloads)
                                {
                                    foreach (var pd in header.FileMetadata.Payloads)
                                    {
                                        pd.PreviewThumbnail = null;
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug("AppData in File {file} on drive {drive} is null.  FileState: {fs}", file.FileId, file.DriveId,
                                header?.FileState);
                        }

                        if (options.ExcludeServerMetaData && null != header)
                        {
                            header.ServerMetadata = null;
                        }
                        else
                        {
                            if (!options.IncludeTransferHistory && header.ServerMetadata != null)
                            {
                                // _logger.LogDebug("CreateClientFileHeaders -> setting transfer history to null");
                                header.ServerMetadata.TransferHistory = null;
                            }
                        }

                        results.Add(header);
                    }
                    else
                    {
                        var drive = await DriveManager.GetDriveAsync(file.DriveId);
                        
                        // Allow anon will let the user get the file so only log
                        // if this is not the case as it means we have a problem
                        if (!drive.AllowAnonymousReads)
                        {
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
            }

            return results;
        }

        /// <summary>
        /// Gets the file Id a file by its <see cref="GlobalTransitIdFileIdentifier"/>
        /// </summary>
        /// <returns>The fileId; otherwise null if the file does not exist</returns>
        private async Task<QueryBatchResult> GetBatchInternal(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false)
        {
            var drive = await DriveManager.GetDriveAsync(driveId);
            var (cursor, fileIdList, hasMoreRows) = await _driveQuery.GetBatchCoreAsync(
                drive,
                odinContext,
                GetFileSystemType(),
                qp,
                options);

            _logger.LogInformation("Found {fc} files in db", fileIdList.Count());

            var headers = await CreateClientFileHeadersAsync(driveId, fileIdList, options, odinContext, forceIncludeServerMetadata);

            _logger.LogInformation("Loaded header count {fc}", headers.Count());

            return new QueryBatchResult()
            {
                QueryTime = UnixTimeUtc.Now().milliseconds,
                IncludeMetadataHeader = options.IncludeHeaderContent,
                Cursor = cursor,
                SearchResults = headers,
                HasMoreRows = hasMoreRows
            };
        }
    }

    public class UniqueIdDump
    {
        public List<DumpResult> Results { get; set; } = new List<DumpResult>();
    }

    public class DumpResult
    {
        public List<DriveMainIndexRecord> RecordList { get; set; }
        public string Name { get; set; }
    }
}