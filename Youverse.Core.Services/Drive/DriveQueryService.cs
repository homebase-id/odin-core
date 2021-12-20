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
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Drive
{
    public class DriveQueryService : IDriveQueryService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;
        private readonly ConcurrentDictionary<Guid, IDriveIndexManager> _indexManagers;

        private readonly IGranteeResolver _granteeResolver;
        private readonly IStorageManager _storageManager;
        private readonly DotYouContext _context;

        private readonly ILogger<object> _logger;

        //HACK: total hack.  define the data attributes as a fixed drive until we move them to use the actual storage 
        private readonly IProfileAttributeManagementService _profileSvc;

        public DriveQueryService(IDriveService driveService, ISystemStorage systemStorage, IProfileAttributeManagementService profileSvc, IGranteeResolver granteeResolver, IStorageManager storageManager, DotYouContext context, ILogger<object> logger)
        {
            _driveService = driveService;
            _systemStorage = systemStorage;
            _profileSvc = profileSvc;
            _granteeResolver = granteeResolver;
            _storageManager = storageManager;
            _context = context;
            _logger = logger;
            _indexManagers = new ConcurrentDictionary<Guid, IDriveIndexManager>();

            InitializeQueryServices();
        }

        public Task RebuildAllIndices()
        {
            //TODO: optimize by making this parallel processed or something
            foreach (var qs in _indexManagers.Values)
            {
                qs.RebuildIndex();
            }

            return Task.CompletedTask;
        }

        public Task RebuildIndex(Guid driveId)
        {
            if (TryGetOrLoadIndexManager(driveId, out var manager, onlyReadyManagers: false).GetAwaiter().GetResult())
            {
                manager.RebuildIndex();
            }

            return Task.CompletedTask;
        }
        

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, PageOptions pageOptions)
        {
            if (await TryGetOrLoadIndexManager(driveId, out var indexManager))
            {
                return await indexManager.GetRecentlyCreatedItems(includeContent, pageOptions);
            }

            throw new NoValidIndexException(driveId);
        }

        public async Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, PageOptions pageOptions)
        {
            if (await TryGetOrLoadIndexManager(driveId, out var indexManager))
            {
                return await indexManager.GetItemsByCategory(categoryId, includeContent, pageOptions);
            }

            throw new NoValidIndexException(driveId);
        }

        private async void InitializeQueryServices()
        {
            var allDrives = await _driveService.GetDrives(new PageOptions(1, Int32.MaxValue));
            foreach (var drive in allDrives.Results)
            {
                await this.LoadIndexManager(drive, out var _);
            }
        }

        private Task<bool> TryGetOrLoadIndexManager(Guid driveId, out IDriveIndexManager manager, bool onlyReadyManagers = true)
        {
            if (_indexManagers.TryGetValue(driveId, out manager))
            {
                if (onlyReadyManagers && manager.IndexReadyState == IndexReadyState.NotAvailable)
                {
                    manager = null;
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }

            //HACK: 
            if (driveId == ProfileIndexManager.DataAttributeDriveId)
            {
                var pDrive = new StorageDrive(_context.StorageConfig.DataStoragePath, _context.StorageConfig.TempStoragePath, new StorageDriveBase()
                {
                    Id = driveId,
                    Name = "profile hack"
                });

                manager = new ProfileIndexManager(pDrive, _systemStorage, _profileSvc, _granteeResolver, _storageManager, _logger);

                //add it first in case load latest fails.  we want to ensure the rebuild process can still access this manager to rebuild its index
                _indexManagers.TryAdd(driveId, manager);
                manager.LoadLatestIndex().GetAwaiter().GetResult();

                return Task.FromResult(true);
            }

            var drive = _driveService.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            LoadIndexManager(drive, out manager);

            if (onlyReadyManagers && manager.IndexReadyState == IndexReadyState.NotAvailable)
            {
                manager = null;
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private Task LoadIndexManager(StorageDrive drive, out IDriveIndexManager manager)
        {
            manager = new LiteDbDriveIndexManager(drive, _systemStorage, _granteeResolver, _storageManager, _logger);

            //add it first in case load latest fails.  we want to ensure the rebuild process can still access this manager to rebuild its index
            _indexManagers.TryAdd(drive.Id, manager);
            manager.LoadLatestIndex().GetAwaiter().GetResult();

            return Task.CompletedTask;
        }
    }
}