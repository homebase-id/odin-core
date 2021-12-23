using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Profile;

namespace Youverse.Core.Services.Drive
{
    public class DriveQueryService : IDriveQueryService
    {
        private readonly IDriveService _driveService;
        private readonly ConcurrentDictionary<Guid, IDriveQueryManager> _queryManagers;
        private readonly IGranteeResolver _granteeResolver;
        private readonly ILoggerFactory _loggerFactory;
        
        public DriveQueryService(IDriveService driveService, IGranteeResolver granteeResolver, ILoggerFactory loggerFactory)
        {
            _driveService = driveService;
            _granteeResolver = granteeResolver;
            _loggerFactory = loggerFactory;
            _queryManagers = new ConcurrentDictionary<Guid, IDriveQueryManager>();

            _driveService.FileChanged += DriveServiceOnFileMetaDataChanged;
            
            InitializeQueryManagers();
        }

        private void DriveServiceOnFileMetaDataChanged(object? sender, DriveFileChangedArgs e)
        {
            // var stream = _driveService.GetFilePartStream(e.File, FilePart.Metadata, StorageDisposition.LongTerm).GetAwaiter().GetResult();
            //_driveService.GetMetadata(e.File, StorageDisposition.LongTerm);
            
            this.TryGetOrLoadQueryManager(e.File.DriveId, out var manager, false);
            manager.UpdateIndex(e.File, e.FileMetaData);
        }

        private async Task RebuildIndex(Guid driveId)
        {
            var metaDataList = await _driveService.GetMetadataFiles(driveId);
            
            await this.TryGetOrLoadQueryManager(driveId, out var manager, false);
            
            foreach (FileMetaData md in metaDataList)
            {
                manager.UpdateIndex(file, md);
            }
        }

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, PageOptions pageOptions)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                return await queryManager.GetRecentlyCreatedItems(includeContent, pageOptions);
            }

            throw new NoValidIndexException(driveId);
        }

        public async Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, PageOptions pageOptions)
        {
            if (await TryGetOrLoadQueryManager(driveId, out var queryManager))
            {
                return await queryManager.GetItemsByCategory(categoryId, includeContent, pageOptions);
            }

            throw new NoValidIndexException(driveId);
        }

        private async void InitializeQueryManagers()
        {
            var allDrives = await _driveService.GetDrives(new PageOptions(1, Int32.MaxValue));
            foreach (var drive in allDrives.Results)
            {
                await this.LoadQueryManager(drive, out var _);
            }
        }

        private Task<bool> TryGetOrLoadQueryManager(Guid driveId, out IDriveQueryManager manager, bool onlyReadyManagers = true)
        {
            if (_queryManagers.TryGetValue(driveId, out manager))
            {
                if (onlyReadyManagers && manager.IndexReadyState == IndexReadyState.NotAvailable)
                {
                    manager = null;
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }

            var drive = _driveService.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            LoadQueryManager(drive, out manager);

            if (onlyReadyManagers && manager.IndexReadyState == IndexReadyState.NotAvailable)
            {
                manager = null;
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private Task LoadQueryManager(StorageDrive drive, out IDriveQueryManager manager)
        {
            var logger = _loggerFactory.CreateLogger<IDriveQueryManager>();
            manager = new LiteDbDriveQueryManager(drive, logger);

            //add it first in case load latest fails.  we want to ensure the
            //rebuild process can still access this manager to rebuild its index
            _queryManagers.TryAdd(drive.Id, manager);

            var index = _driveService.GetCurrentIndex(drive.Id);
            manager.SetCurrentIndex(index);

            return Task.CompletedTask;
        }
    }
}