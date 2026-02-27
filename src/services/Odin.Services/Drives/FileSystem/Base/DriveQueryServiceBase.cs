using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
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

        protected DriveQueryServiceBase(
            ILogger logger,
            IDriveManager driveManager,
            DriveQuery driveQuery,
            DriveStorageServiceBase storage
        )
        {
            _logger = logger;
            _driveQuery = driveQuery;
            DriveManager = driveManager;
            _storage = storage;
        }

        protected override IDriveManager DriveManager { get; }

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> the inheriting class manages
        /// </summary>
        protected abstract FileSystemType GetFileSystemType();

        public async Task<DriveSizeInfo> GetDriveSize(Guid driveId, IOdinContext odinContext)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var drive = await DriveManager.GetDriveAsync(driveId, failIfInvalid: true);
            var (fileCount, bytes) = await _driveQuery.GetDriveSizeInfoAsync(drive);

            return new DriveSizeInfo()
            {
                FileCount = fileCount,
                Size = bytes
            };
        }

        public async Task<QueryModifiedResult> GetModified(Guid driveId, FileQueryParamsV1 qp, QueryModifiedResultOptions options,
            IOdinContext odinContext)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);
            await AssertCanReadDriveAsync(driveId, odinContext);

            var o = options ?? QueryModifiedResultOptions.Default();

            var drive = await DriveManager.GetDriveAsync(driveId, failIfInvalid: true);
            var (updatedCursor, recordList, hasMoreRows) =
                await _driveQuery.GetModifiedCoreAsync(drive, odinContext, GetFileSystemType(), qp, o);

            var (headers, _) = await CreateClientFileHeadersAsync(driveId, recordList, o, odinContext);

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
            await AssertDriveIsValidAndActive(driveId, odinContext);
            await AssertCanReadDriveAsync(driveId, odinContext);
            return await GetBatchInternal(driveId, qp, options, odinContext, forceIncludeServerMetadata);
        }

        public async Task<QueryBatchResult> GetSmartBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);
            await AssertCanReadDriveAsync(driveId, odinContext);
            var drive = await DriveManager.GetDriveAsync(driveId);
            var (cursor, fileIdList, hasMoreRows) = await _driveQuery.GetSmartBatchCoreAsync(
                drive,
                odinContext,
                GetFileSystemType(),
                qp,
                options);

            var (headers, _) = await CreateClientFileHeadersAsync(driveId, fileIdList, options, odinContext, forceIncludeServerMetadata);

            return new QueryBatchResult()
            {
                QueryTime = UnixTimeUtc.Now().milliseconds,
                IncludeMetadataHeader = options.IncludeHeaderContent,
                Cursor = cursor,
                SearchResults = headers,
                HasMoreRows = hasMoreRows
            };
        }

        public async Task<SharedSecretEncryptedFileHeader> GetSingleFileByTag(Guid driveId, Guid tag, IOdinContext odinContext)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var qp = new FileQueryParamsV1()
            {
                TagsMatchAll = [tag]
            };

            var options = new QueryBatchResultOptions()
            {
                MaxRecords = 1,
                IncludeHeaderContent = true,
                ExcludePreviewThumbnail = false
            };

            var results = await this.GetBatch(driveId, qp, options, odinContext);
            return results.SearchResults.SingleOrDefault();
        }

        /// <summary>
        /// Permissions check allows you to get the file if you only have DrivePermission.Write
        /// </summary>
        public async Task<SharedSecretEncryptedFileHeader> GetFileByClientUniqueIdForWriting(Guid driveId, Guid clientUniqueId,
            IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);
            var options = new ResultOptions()
            {
                MaxRecords = 1,
                IncludeHeaderContent = true,
                ExcludePreviewThumbnail = true
            };

            return await GetFileByClientUniqueIdInternal(driveId, clientUniqueId, options, odinContext);
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId,
            ResultOptions options,
            IOdinContext odinContext)
        {
            await AssertCanReadDriveAsync(driveId, odinContext);
            return await GetFileByClientUniqueIdInternal(driveId, clientUniqueId, options, odinContext);
        }

        public async Task<QueryBatchCollectionResponse> GetBatchCollection(List<CollectionQueryParamSection> queries,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false)
        {
            foreach (var query in queries)
            {
                var driveId = query.QueryParams.DriveId;
                await AssertDriveIsValidAndActive(driveId, odinContext);
            }

            if (queries.DistinctBy(q => q.Name).Count() != queries.Count())
            {
                throw new OdinClientException("The Names of Queries must be unique", OdinClientErrorCode.InvalidQuery);
            }

            var permissionContext = odinContext.PermissionsContext;

            var collection = new QueryBatchCollectionResponse();
            foreach (var query in queries)
            {
                var driveId = query.QueryParams.DriveId;
                var canReadDrive = permissionContext.HasDrivePermission(driveId, DrivePermission.Read);
                if (canReadDrive)
                {
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

        public async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false,
            bool excludePreviewThumbnail = true,
            bool includeTransferHistory = true)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var record = await _driveQuery.GetByGlobalTransitIdAsync(driveId, globalTransitId);

            if (null == record)
            {
                _logger.LogWarning($"DriveQueryServiceBase.GetFileByGlobalTransitId returned " +
                                   $"null driveId:{driveId} gtid:{globalTransitId}");
                return null;
            }

            if (record.fileSystemType != (int)GetFileSystemType())
            {
                _logger.LogWarning("Mismatching file system type");
                return null;
            }

            var options = new ResultOptions()
            {
                MaxRecords = 10,
                ExcludePreviewThumbnail = excludePreviewThumbnail,
                IncludeHeaderContent = true,
                IncludeTransferHistory = includeTransferHistory
            };

            var (headers, aclFailures) = await CreateClientFileHeadersAsync(driveId, [record], options, odinContext,
                forceIncludeServerMetadata, logAclFailuresAsErrors: true);

            var theSingleLonelyResult = headers.SingleOrDefault();
            if (theSingleLonelyResult == null)
            {
                // see if it's because of a permissions issue
                if (aclFailures.Any(f => f.FileMetadata.GlobalTransitId == globalTransitId))
                {
                    throw new OdinSecurityException($"Cannot access file with globalTransitId: {globalTransitId}");
                }
            }

            return theSingleLonelyResult;
        }

        public async Task<InternalDriveFileId?> ResolveFileId(GlobalTransitIdFileIdentifier file, IOdinContext odinContext)
        {
            var driveId = file.TargetDrive.Alias;
            await AssertDriveIsValidAndActive(driveId, odinContext);
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);

            var record = await _driveQuery.GetByGlobalTransitIdAsync(driveId, file.GlobalTransitId);

            if (null == record)
            {
                return null;
            }

            if (record.fileSystemType != (int)GetFileSystemType())
            {
                return null;
            }

            return new InternalDriveFileId()
            {
                FileId = record.fileId,
                DriveId = driveId
            };
        }

        private async Task<(
                IEnumerable<SharedSecretEncryptedFileHeader> results,
                IEnumerable<ServerFileHeader> aclFailures)>
            CreateClientFileHeadersAsync(
                Guid driveId,
                List<DriveMainIndexRecord> recordList,
                ResultOptions options,
                IOdinContext odinContext,
                bool forceIncludeServerMetadata = false,
                bool logAclFailuresAsErrors = false)
        {
            var results = new List<SharedSecretEncryptedFileHeader>();
            var aclFailures = new List<ServerFileHeader>();

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
                    _logger.LogError("File {file} on drive {drive} was found in index but was not returned from disk",
                        file.FileId,
                        file.DriveId);
                    continue;
                }

                // TODD - this function ALSO loads the header from disk. It needs to use 'record' instead.
                var hasPermissionToFile = await _storage.CallerHasPermissionToFile(serverFileHeader, odinContext);
                if (!hasPermissionToFile)
                {
                    aclFailures.Add(serverFileHeader);
                    if (logAclFailuresAsErrors)
                    {
                        _logger.LogWarning($"Caller with OdinId [{odinContext.Caller.OdinId}] received the file from the drive" +
                                           $" search index but does not have read access to the file:{file.FileId} on drive:{file.DriveId}");
                    }
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

                    //Note: it is possible that an app can have read access to a drive that allows anonymous but not have the storage key   
                    var shouldReceiveFile = (isEncrypted && hasStorageKey) || !isEncrypted;

                    _logger.LogDebug($"hasStorageKey: {hasStorageKey} | shouldReceiveFile: " +
                                     $"{shouldReceiveFile} | is encrypted: {isEncrypted}");

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

            return (results, aclFailures);
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

            var (headers, _) = await CreateClientFileHeadersAsync(driveId, fileIdList, options, odinContext, forceIncludeServerMetadata);

            return new QueryBatchResult()
            {
                QueryTime = UnixTimeUtc.Now().milliseconds,
                IncludeMetadataHeader = options.IncludeHeaderContent,
                Cursor = cursor,
                SearchResults = headers,
                HasMoreRows = hasMoreRows
            };
        }

        private async Task AssertDriveIsValidAndActive(Guid driveId, IOdinContext odinContext)
        {
            var theDrive = await DriveManager.GetDriveAsync(driveId, failIfInvalid: true);
            if (theDrive.IsArchived)
            {
                if (!odinContext.Caller.HasMasterKey)
                {
                    throw new OdinClientException("Drive is archived", OdinClientErrorCode.InvalidDrive);
                }
            }
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByClientUniqueIdInternal(Guid driveId, Guid clientUniqueId,
            ResultOptions options, IOdinContext odinContext)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);

            var record = await _driveQuery.GetByClientUniqueIdAsync(driveId, clientUniqueId);

            if (null == record)
            {
                return null;
            }

            if (record.fileSystemType != (int)GetFileSystemType())
            {
                return null;
            }

            var (headers, aclFailures) = await CreateClientFileHeadersAsync(driveId, [record], options, odinContext,
                logAclFailuresAsErrors: false);
            var theSingleLonelyResult = headers.SingleOrDefault();
            if (theSingleLonelyResult == null)
            {
                // see if it's because of a permissions issue
                if (aclFailures.Any(f => f.FileMetadata.AppData.UniqueId == clientUniqueId))
                {
                    throw new OdinSecurityException($"Cannot access file with uniqueId: {clientUniqueId}");
                }
            }

            return theSingleLonelyResult;
        }
    }
}