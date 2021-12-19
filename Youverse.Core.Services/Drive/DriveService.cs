using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Profile;

namespace Youverse.Core.Services.Drive
{
    public class DriveService : IDriveService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveManager _driveManager;
        private readonly ConcurrentDictionary<Guid, IDriveIndexManager> _indexManagers;

        private readonly IProfileAttributeManagementService _profileSvc;
        private readonly IGranteeResolver _granteeResolver;
        private readonly IStorageManager _storageManager;

        public DriveService(IDriveManager driveManager, ISystemStorage systemStorage, IProfileAttributeManagementService profileSvc, IGranteeResolver granteeResolver, IStorageManager storageManager)
        {
            _driveManager = driveManager;
            _systemStorage = systemStorage;
            _profileSvc = profileSvc;
            _granteeResolver = granteeResolver;
            _storageManager = storageManager;
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

        public IStorageManager StorageManager => this._storageManager;

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, PageOptions pageOptions)
        {
            if (TryGetOrLoadIndexManager(driveId, out var indexManager).GetAwaiter().GetResult() && indexManager.IndexReadyState == IndexReadyState.Ready)
            {
                return await indexManager.GetRecentlyCreatedItems(includeContent, pageOptions);
            }

            throw new NoValidIndexException(driveId);
        }

        public async Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, PageOptions pageOptions)
        {
            if (TryGetOrLoadIndexManager(driveId, out var indexManager).GetAwaiter().GetResult())
            {
                return await indexManager.GetItemsByCategory(categoryId, includeContent, pageOptions);
            }

            throw new NoValidIndexException(driveId);
        }

        private async void InitializeQueryServices()
        {
            var allDrives = await _driveManager.GetDrives(new PageOptions(1, Int32.MaxValue));
            foreach (var drive in allDrives.Results)
            {
                await this.LoadIndexManager(drive, out var _);
            }
        }

        private Task<bool> TryGetOrLoadIndexManager(Guid driveId, out IDriveIndexManager manager, bool onlyReadyManagers = true)
        {
            if (_indexManagers.TryGetValue(driveId, out manager))
            {
                if (onlyReadyManagers && manager.IndexReadyState != IndexReadyState.NotAvailable)
                {
                    manager = null;
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }

            var drive = _driveManager.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            LoadIndexManager(drive, out manager);

            if (onlyReadyManagers && manager.IndexReadyState != IndexReadyState.NotAvailable)
            {
                manager = null;
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private Task LoadIndexManager(StorageDrive drive, out IDriveIndexManager manager)
        {
            manager = new LiteDbDriveIndexManager(drive, _systemStorage, _profileSvc, _granteeResolver, _storageManager);

            //add it first in case load latest fails.  we want to ensure the rebuild process can still access this manager to rebuild its index
            _indexManagers.TryAdd(drive.Id, manager);
            manager.LoadLatestIndex().GetAwaiter().GetResult();

            return Task.CompletedTask;
        }
    }
}