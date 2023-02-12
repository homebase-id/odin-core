using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Core;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Drive
{
    public class DriveQueryService : IDriveQueryService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly StandardFileDriveService _driveService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IAppService _appService;

        private readonly DriveDatabaseHost _driveDatabaseHost;

        public DriveQueryService(StandardFileDriveService driveService, ILoggerFactory loggerFactory, DotYouContextAccessor contextAccessor, IAppService appService, DriveDatabaseHost driveDatabaseHost)
        {
            _driveService = driveService;
            _loggerFactory = loggerFactory;
            _contextAccessor = contextAccessor;
            _appService = appService;
            _driveDatabaseHost = driveDatabaseHost;
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

            throw new NoValidIndexClientException(driveId);
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

            throw new NoValidIndexClientException(driveId);
        }

        public async Task<ClientFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId)
        {
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

        public Task EnqueueCommandMessage(Guid driveId, List<Guid> fileIds)
        {
            TryGetOrLoadQueryManager(driveId, out var manager);
            return manager.AddCommandMessage(fileIds);
        }

        public async Task<List<ReceivedCommand>> GetUnprocessedCommands(Guid driveId, int count)
        {
            await TryGetOrLoadQueryManager(driveId, out var manager);
            var unprocessedCommands = await manager.GetUnprocessedCommands(count);

            var result = new List<ReceivedCommand>();

            foreach (var cmd in unprocessedCommands)
            {
                var file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = cmd.Id
                };

                var commandFileHeader = await _appService.GetClientEncryptedFileHeader(file);
                var command = DotYouSystemSerializer.Deserialize<CommandTransferMessage>(commandFileHeader.FileMetadata.AppData.JsonContent);

                result.Add(new ReceivedCommand()
                {
                    Id = commandFileHeader.FileId, //TODO: should this be the ID?
                    Sender = commandFileHeader.FileMetadata.SenderDotYouId,
                    ClientCode = commandFileHeader.FileMetadata.AppData.DataType,
                    ClientJsonMessage = command.ClientJsonMessage,
                    GlobalTransitIdList = command!.GlobalTransitIdList
                });
            }

            return result;
        }

        public async Task MarkCommandsProcessed(Guid driveId, List<Guid> idList)
        {
            await TryGetOrLoadQueryManager(driveId, out var manager);
            await manager.MarkCommandsCompleted(idList);
        }

        public async Task<QueryBatchCollectionResponse> GetBatchCollection(QueryBatchCollectionRequest request)
        {
            var collection = new QueryBatchCollectionResponse();
            foreach (var query in request.Queries)
            {
                var driveId = (await _driveService.GetDriveIdByAlias(query.QueryParams.TargetDrive, true)).GetValueOrDefault();
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
                if (this.TryGetOrLoadQueryManager(driveId, out var manager, false).GetAwaiter().GetResult())
                {
                    manager.EnsureIndexDataCommitted();
                }
            }

            return Task.CompletedTask;
        }

        public async Task<ClientFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId)
        {
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

        private Task<bool> TryGetOrLoadQueryManager(Guid driveId, out IDriveQueryManager manager, bool onlyReadyManagers = true)
        {
            return _driveDatabaseHost.TryGetOrLoadQueryManager(driveId, out manager, onlyReadyManagers);
        }
        
        public void Dispose()
        {
        }
    }
}