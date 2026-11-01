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

        /// <summary>
        /// Returns the newest server-set modified timestamp (ms) of an active file on the drive, or 0 when the
        /// drive has no files. Intentionally NOT clamped to the temporal window — it's a single status timestamp
        /// (never content) so a caller can detect e.g. that data updates have stopped. Requires temporal-or-full read.
        /// </summary>
        public async Task<long> GetNewestFileModified(Guid driveId, IOdinContext odinContext)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);
            odinContext.PermissionsContext.AssertCanConditionalTemporalReadDrive(driveId);
            return await _driveQuery.GetNewestModifiedAsync(driveId, (int)GetFileSystemType());
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

        /// <summary>
        /// Temporal (time-boxed) batch query. The ONLY query path that honors
        /// <see cref="DrivePermission.ConditionalTemporalRead"/>: it asserts the temporal flag (not Read)
        /// and clamps results to files whose server-set modified date is within the resolved window.
        /// </summary>
        public async Task<QueryBatchResult> GetTemporalBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false)
        {
            await AssertDriveIsValidAndActive(driveId, odinContext);
            odinContext.PermissionsContext.AssertCanConditionalTemporalReadDrive(driveId);

            var drive = await DriveManager.GetDriveAsync(driveId, failIfInvalid: true);
            var modifiedAfter = TemporalReadPolicy.ResolveCutoff(odinContext, drive);

            return await GetBatchInternal(driveId, qp, options, odinContext, forceIncludeServerMetadata, modifiedAfter);
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

            return await CreateBatchResultAsync(driveId, cursor, fileIdList, hasMoreRows, options, odinContext,
                forceIncludeServerMetadata);
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

        /// <summary>
        /// Metadata-only existence lookup by client unique id. Returns the raw
        /// <see cref="ServerFileHeader"/> for the matching file (or null) without applying the
        /// per-file ACL. Drive-level Read or Write is still required.
        /// </summary>
        /// <remarks>
        /// Intended for "is there a file with this id on this drive?" endpoints — for those,
        /// the drive-level permission is the correct gate; per-file ACL governs content access
        /// and is the wrong question.
        ///
        /// Concretely, this method exists to support the peer file-exists endpoint used by
        /// heal-a-group flows. <see cref="GetFileByClientUniqueId"/> filters through the
        /// per-file ACL and throws <see cref="OdinSecurityException"/> when it denies — and
        /// <c>PeerFileWriter</c> rewrites the recipient's stored ACL to
        /// <c>SecurityGroupType.Owner</c> on receipt of a transit transfer (see
        /// <c>PeerFileWriter.cs</c>). So the original author asking the recipient about their
        /// own file via the normal query path would always be denied. That's correct for
        /// content reads but wrong for an existence probe, which only needs to gate on whether
        /// the caller has any drive access at all.
        /// </remarks>
        public async Task<ServerFileHeader> GetServerFileHeaderByClientUniqueIdSkippingFileAcl(Guid driveId, Guid clientUniqueId,
            IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);
            await AssertDriveIsValidAndActive(driveId, odinContext);

            var record = await _driveQuery.GetByClientUniqueIdAsync(driveId, clientUniqueId);
            if (record == null || record.fileSystemType != (int)GetFileSystemType())
            {
                return null;
            }

            return ServerFileHeader.FromDriveMainIndexRecord(record);
        }

        /// <inheritdoc cref="GetServerFileHeaderByClientUniqueIdSkippingFileAcl"/>
        public async Task<ServerFileHeader> GetServerFileHeaderByGlobalTransitIdSkippingFileAcl(Guid driveId, Guid globalTransitId,
            IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(driveId, odinContext);
            await AssertDriveIsValidAndActive(driveId, odinContext);

            var record = await _driveQuery.GetByGlobalTransitIdAsync(driveId, globalTransitId);
            if (record == null || record.fileSystemType != (int)GetFileSystemType())
            {
                return null;
            }

            return ServerFileHeader.FromDriveMainIndexRecord(record);
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
                    collection.Results.Add(QueryBatchResponse.FromInvalidDrive(query.Name,
                        query.ResultOptionsRequest?.ResultShape == FileResultShape.Minimal));
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
                return null;
            }

            if (record.fileSystemType != (int)GetFileSystemType())
            {
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
                forceIncludeServerMetadata, logAclFailuresAsErrors: false);

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
            var (visible, aclFailures, sharedWithCaller) = await FilterVisibleAsync(driveId, recordList, odinContext, logAclFailuresAsErrors);

            var results = new List<SharedSecretEncryptedFileHeader>();
            foreach (var serverFileHeader in visible)
            {
                var header = DriveFileUtility.CreateClientFileHeader(
                    serverFileHeader,
                    odinContext,
                    forceIncludeServerMetadata,
                    sharedWithCaller?.GetValueOrDefault(serverFileHeader.FileMetadata.File.FileId));

                if (header?.FileMetadata?.AppData != null)
                {
                    if (!options.IncludeHeaderContent)
                    {
                        header.FileMetadata.AppData = header.FileMetadata.AppData.WithoutContent();
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
                    _logger.LogDebug("AppData in File {file} on drive {drive} is null.  FileState: {fs}",
                        serverFileHeader.FileMetadata.File.FileId, driveId, header?.FileState);
                }

                if (options.ExcludeServerMetaData && null != header)
                {
                    header.ServerMetadata = null;
                }
                else if (!options.IncludeTransferHistory && header.ServerMetadata != null)
                {
                    header.ServerMetadata.TransferHistory = null;
                }

                results.Add(header);
            }

            if (options.IncludeShares)
            {
                await _storage.AttachSharesAsync(driveId, results, odinContext);
            }

            return (results, aclFailures);
        }

        /// <summary>
        /// The rows of <paramref name="recordList"/> the caller may receive, in order, and the ones its ACL denied.
        /// Every result shape goes through here, so none of them can show a file another would hide.  On a Selective
        /// Share drive, also the share each visible file reached an outside caller through (null otherwise).
        /// </summary>
        private async Task<(List<ServerFileHeader> visible, List<ServerFileHeader> aclFailures,
            IReadOnlyDictionary<Guid, DriveAclIndexRecord> sharedWithCaller)> FilterVisibleAsync(
            Guid driveId,
            List<DriveMainIndexRecord> recordList,
            IOdinContext odinContext,
            bool logAclFailuresAsErrors = false)
        {
            var visible = new List<ServerFileHeader>();
            var aclFailures = new List<ServerFileHeader>();

            // On a Selective Share drive, the page's share rows in one read rather than one per file
            var sharedWithCaller = await _storage.FilesSharedWithCallerAsync(driveId,
                recordList.Select(r => r.fileId).ToList(), odinContext);

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

                // The index has no expiry column (deliberately - see FileTtl), so a file whose TTL has
                // come due can still be sitting in query results until its job runs. Drop it here for the
                // same reason the by-id read path does: jobs lag, and an expired file must not be served.
                if (DriveStorageServiceBase.IsExpired(serverFileHeader))
                {
                    continue;
                }

                var isClientLocked = ZkDriveFiles.IsClientLocked(serverFileHeader);
                if (!await _storage.CallerMayReadFileOnLiveDrive(serverFileHeader, odinContext, sharedWithCaller))
                {
                    // A client-locked file (sealed or zkDrive) is left out as missing, not reported as refused: a
                    // read by unique id or global transit id answers as for no file, and a write's check for a taken
                    // unique id falls through to the index's
                    if (isClientLocked)
                    {
                        continue;
                    }

                    aclFailures.Add(serverFileHeader);
                    if (logAclFailuresAsErrors)
                    {
                        _logger.LogError($"Caller with OdinId [{odinContext.Caller.OdinId}] received the file from the drive" +
                                         $" search index but does not have read access to the file:{file.FileId} on drive:{file.DriveId}");
                    }
                }
                else
                {
                    if (serverFileHeader.FileMetadata.FileState == FileState.Deleted)
                    {
                        // Off: one line per deleted row of every query, so a client paging a feed of tombstones (e.g. filesync
                        // after a purge) made it 96% of the dev server's log, about 1 GB every 20 minutes, and filled the disk
                        // _logger.LogDebug("Creating Client File Header for deleted file (File {file} on drive {drive})", file.FileId,
                        //     file.DriveId);
                        // var header = DriveFileUtility.CreateDeletedClientFileHeader(serverFileHeader, odinContext);
                        // results.Add(header);
                        // continue;
                    }

                    var isEncrypted = serverFileHeader.FileMetadata.IsEncrypted;
                    var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(file.DriveId, out _);

                    //Note: it is possible that an app can have read access to a drive that allows anonymous but not have the storage key
                    // A client-locked file needs no storage key to return: its key header goes back as stored
                    var shouldReceiveFile = (isEncrypted && hasStorageKey) || !isEncrypted || isClientLocked;
                    if (shouldReceiveFile)
                    {
                        visible.Add(serverFileHeader);
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

            return (visible, aclFailures, sharedWithCaller);
        }

        /// <summary>
        /// Gets the file Id a file by its <see cref="GlobalTransitIdFileIdentifier"/>
        /// </summary>
        /// <returns>The fileId; otherwise null if the file does not exist</returns>
        private async Task<QueryBatchResult> GetBatchInternal(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options,
            IOdinContext odinContext,
            bool forceIncludeServerMetadata = false,
            UnixTimeUtc? modifiedAfter = null)
        {
            var drive = await DriveManager.GetDriveAsync(driveId);
            var (cursor, fileIdList, hasMoreRows) = await _driveQuery.GetBatchCoreAsync(
                drive,
                odinContext,
                GetFileSystemType(),
                qp,
                options,
                modifiedAfter);

            return await CreateBatchResultAsync(driveId, cursor, fileIdList, hasMoreRows, options, odinContext,
                forceIncludeServerMetadata);
        }

        // The cursor and hasMoreRows come from the query, before any row is filtered, so every result shape of the
        // same query pages identically; only what each visible row carries differs
        private async Task<QueryBatchResult> CreateBatchResultAsync(Guid driveId, QueryBatchCursor cursor,
            List<DriveMainIndexRecord> records, bool hasMoreRows, QueryBatchResultOptions options, IOdinContext odinContext,
            bool forceIncludeServerMetadata)
        {
            var result = new QueryBatchResult
            {
                QueryTime = UnixTimeUtc.Now().milliseconds,
                IncludeMetadataHeader = options.IncludeHeaderContent,
                Cursor = cursor,
                HasMoreRows = hasMoreRows
            };

            if (options.ResultShape == FileResultShape.Minimal)
            {
                var (visible, _, _) = await FilterVisibleAsync(driveId, records, odinContext);
                result.FileRefs = visible.Select(FileRef.FromHeader).ToList();
            }
            else
            {
                var (headers, _) = await CreateClientFileHeadersAsync(driveId, records, options, odinContext, forceIncludeServerMetadata);
                result.SearchResults = headers;
            }

            return result;
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