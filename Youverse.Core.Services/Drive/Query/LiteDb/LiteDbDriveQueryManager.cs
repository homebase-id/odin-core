using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public class LiteDbDriveQueryManager : IDriveQueryManager
    {
        private LiteDBSingleCollectionStorage<IndexedItem> _indexStorage;
        private LiteDBSingleCollectionStorage<IndexedItem> _backupIndexStorage;
        private StorageDriveIndex _currentIndex;
        private IndexReadyState _indexReadyState;
        private readonly ILogger<object> _logger;

        private readonly StorageDriveIndex _primaryIndex;
        private readonly StorageDriveIndex _secondaryIndex;

        private readonly string IndexCollectionName = "index";

        private readonly IHttpContextAccessor _accessor;

        public LiteDbDriveQueryManager(StorageDrive drive, ILogger<object> logger, IHttpContextAccessor accessor)
        {
            _logger = logger;
            _accessor = accessor;
            this.Drive = drive;

            _primaryIndex = new StorageDriveIndex(IndexTier.Primary, drive.GetIndexPath());
            _secondaryIndex = new StorageDriveIndex(IndexTier.Secondary, drive.GetIndexPath());
        }

        public IndexReadyState IndexReadyState
        {
            get { return this._indexReadyState; }
            set { this._indexReadyState = value; }
        }

        public StorageDrive Drive { get; init; }

        public Task<(byte[], IEnumerable<Guid>)> GetRecent(ulong maxDate, byte[] startCursor, QueryParams qp, ResultOptions options)
        {
            throw new NotImplementedException();
        }

        public Task<(byte[], byte[], ulong, IEnumerable<Guid>)> GetBatch(byte[] startCursor, byte[] stopCursor, QueryParams qp, ResultOptions options)
        {
            throw new NotImplementedException();
        }

        public async Task<PagedResult<Guid>> GetRecentlyCreatedItems(bool includeMetadataHeader,
            PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService)
        {
            AssertValidIndexLoaded();

            lock (_indexStorage)
            {
                //HACK: highly inefficient way to do security filtering (we're scanning all f'kin records)  #prototype
                var unfiltered = _indexStorage.GetList(PageOptions.All,
                        ListSortDirection.Ascending,
                        item => item.CreatedTimestamp)
                    .GetAwaiter().GetResult();

                var filtered = ApplySecurity(unfiltered, pageOptions, driveAclAuthorizationService);
                if (!includeMetadataHeader)
                {
                    StripContent(ref filtered);
                }

                return new PagedResult<Guid>(pageOptions, filtered.TotalPages,
                    filtered.Results.Select(x => x.FileId).ToList());
            }
        }

        public async Task<PagedResult<Guid>> Query(int fileType, int dataType, bool includeMetadataHeader,
            PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService)
        {
            AssertValidIndexLoaded();

            //HACK: grrrr need a better storage engine for searching
            lock (_indexStorage)
            {
                //HACK: highly inefficient way to do security filtering (we're scanning all f'kin records)  #prototype
                // tag == Guid.Empty is for when the tag does not matter. Yes, we need to change the method name to something other than GetByTag 
                var unfiltered = _indexStorage.Find(item => item.FileType == fileType,
                        ListSortDirection.Ascending,
                        item => item.CreatedTimestamp,
                        PageOptions.All)
                    .GetAwaiter()
                    .GetResult();

                var filtered = ApplySecurity(unfiltered, pageOptions, driveAclAuthorizationService);

                if (!includeMetadataHeader)
                {
                    StripContent(ref filtered);
                }

                return new PagedResult<Guid>(pageOptions, filtered.TotalPages,
                    filtered.Results.Select(x => x.FileId).ToList());
            }
        }

        public async Task<PagedResult<Guid>> GetByFileType(int fileType, bool includeMetadataHeader,
            PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService)
        {
            AssertValidIndexLoaded();

            //HACK: grrrr need a better storage engine for searching
            lock (_indexStorage)
            {
                //HACK: highly inefficient way to do security filtering (we're scanning all f'kin records)  #prototype
                // tag == Guid.Empty is for when the tag does not matter. Yes, we need to change the method name to something other than GetByTag 
                var unfiltered = _indexStorage.Find(item => item.FileType == fileType,
                        ListSortDirection.Ascending,
                        item => item.CreatedTimestamp,
                        PageOptions.All)
                    .GetAwaiter()
                    .GetResult();

                var filtered = ApplySecurity(unfiltered, pageOptions, driveAclAuthorizationService);

                if (!includeMetadataHeader)
                {
                    StripContent(ref filtered);
                }

                return new PagedResult<Guid>(pageOptions, filtered.TotalPages,
                    filtered.Results.Select(x => x.FileId).ToList());
            }
        }


        public async Task<PagedResult<Guid>> GetByTag(Guid tag, int fileType, bool includeMetadataHeader,
            PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService)
        {
            AssertValidIndexLoaded();

            //HACK: grrrr need a better storage engine for searching
            lock (_indexStorage)
            {
                //HACK: highly inefficient way to do security filtering (we're scanning all f'kin records)  #prototype
                // tag == Guid.Empty is for when the tag does not matter. Yes, we need to change the method name to something other than GetByTag 
                var unfiltered = _indexStorage.Find(
                        item => (item.Tags.Contains(tag) || tag == Guid.Empty) && item.FileType == fileType,
                        ListSortDirection.Ascending,
                        item => item.CreatedTimestamp,
                        PageOptions.All)
                    .GetAwaiter()
                    .GetResult();

                var filtered = ApplySecurity(unfiltered, pageOptions, driveAclAuthorizationService);

                if (!includeMetadataHeader)
                {
                    StripContent(ref filtered);
                }

                return new PagedResult<Guid>(pageOptions, filtered.TotalPages,
                    filtered.Results.Select(x => x.FileId).ToList());
            }
        }

        public async Task<PagedResult<Guid>> GetByAlias(Guid alias, bool includeMetadataHeader,
            PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService)
        {
            AssertValidIndexLoaded();

            //HACK: grrrr need a better storage engine for searching
            lock (_indexStorage)
            {
                //HACK: highly inefficient way to do security filtering (we're scanning all f'kin records)  #prototype
                var unfiltered = _indexStorage.Find(item => item.Alias == alias,
                        ListSortDirection.Ascending,
                        item => item.CreatedTimestamp,
                        PageOptions.All)
                    .GetAwaiter()
                    .GetResult();

                var filtered = ApplySecurity(unfiltered, pageOptions, driveAclAuthorizationService);

                if (!includeMetadataHeader)
                {
                    StripContent(ref filtered);
                }

                return new PagedResult<Guid>(pageOptions, filtered.TotalPages,
                    filtered.Results.Select(x => x.FileId).ToList());
            }
        }

        private PagedResult<IndexedItem> ApplySecurity(PagedResult<IndexedItem> unfiltered, PageOptions pageOptions,
            IDriveAclAuthorizationService driveAclAuthorizationService)
        {
            Func<IndexedItem, bool> callerHasPermission = (item) =>
                driveAclAuthorizationService.CallerHasPermission(item.AccessControlList).GetAwaiter().GetResult();
            var filtered = unfiltered.Results.Where(callerHasPermission);

            //possible memory spike
            var indexedItems = filtered as IndexedItem[] ?? filtered.ToArray();
            var page = indexedItems!.Skip(pageOptions.GetSkipCount()).Take(pageOptions.PageSize).ToList();
            var results = new PagedResult<IndexedItem>(pageOptions, indexedItems.Count(), page);

            return results;
        }

        public Task SwitchIndex()
        {
            //TODO: do i need to lock here?
            var index = _currentIndex.Tier == IndexTier.Primary ? _secondaryIndex : _primaryIndex;
            SetCurrentIndex(index);
            return Task.CompletedTask;
        }

        public Task UpdateCurrentIndex(FileMetadata metadata)
        {
            _indexStorage.Save(MetadataToIndexedItem(metadata));

            //technically the index is ready because it has at least one item in it
            //this, however, means we need to build a way for apps to understand the
            //actual state of the index so they can notify users
            _indexReadyState = IndexReadyState.Ready;

            return Task.CompletedTask;
        }

        public Task UpdateSecondaryIndex(FileMetadata metadata)
        {
            if (null == _backupIndexStorage)
            {
                throw new Exception(
                    "Backup index not ready; call PrepareSecondaryIndexForRebuild before calling UpdateSecondaryIndex");
            }

            _backupIndexStorage.Save(MetadataToIndexedItem(metadata));

            return Task.CompletedTask;
        }

        public Task RemoveFromCurrentIndex(InternalDriveFileId file)
        {
            _indexStorage.Delete(file.FileId);
            return Task.CompletedTask;
        }

        public Task RemoveFromSecondaryIndex(InternalDriveFileId file)
        {
            _indexStorage.Delete(file.FileId);
            return Task.CompletedTask;
        }

        public Task PrepareSecondaryIndexForRebuild()
        {
            var backupIndex = _currentIndex.Tier == IndexTier.Primary ? _secondaryIndex : _primaryIndex;
            var indexPath = backupIndex.GetQueryIndexPath();
            if (null != _backupIndexStorage)
            {
                _backupIndexStorage.Dispose();
                _backupIndexStorage = null;
            }

            if (Directory.Exists(indexPath))
            {
                Directory.Delete(indexPath, true);
            }

            _backupIndexStorage =
                new LiteDBSingleCollectionStorage<IndexedItem>(_logger, indexPath, IndexCollectionName);

            return Task.CompletedTask;
        }

        public Task LoadLatestIndex()
        {
            //load the most recently used index
            var primaryIsValid = IsValidIndex(_primaryIndex);
            var secondaryIsValid = IsValidIndex(_secondaryIndex);

            if (primaryIsValid && secondaryIsValid)
            {
                var pf = new FileInfo(_primaryIndex.IndexRootPath);
                var sf = new FileInfo(_secondaryIndex.IndexRootPath);
                SetCurrentIndex(pf.CreationTimeUtc >= sf.CreationTimeUtc ? _primaryIndex : _secondaryIndex);
                _indexReadyState = IndexReadyState.Ready;
                return Task.CompletedTask;
            }

            if (primaryIsValid)
            {
                SetCurrentIndex(_primaryIndex);
                _indexReadyState = IndexReadyState.Ready;
                return Task.CompletedTask;
            }

            if (secondaryIsValid)
            {
                SetCurrentIndex(_secondaryIndex);
                _indexReadyState = IndexReadyState.Ready;
                return Task.CompletedTask;
            }

            //neither index is valid; so let us default
            //to primary.  It will be built as files are added.
            SetCurrentIndex(_primaryIndex);
            _indexReadyState = IndexReadyState.RequiresRebuild;

            return Task.CompletedTask;
        }

        private void SetCurrentIndex(StorageDriveIndex index)
        {
            _currentIndex = index;
            if (null != _indexStorage)
            {
                _indexStorage.Dispose();
            }

            var indexPath = _currentIndex.GetQueryIndexPath();
            _indexStorage = new LiteDBSingleCollectionStorage<IndexedItem>(_logger, indexPath, IndexCollectionName);
            _indexReadyState = IndexReadyState.Ready;
        }

        private bool IsValidIndex(StorageDriveIndex index)
        {
            //TODO: this needs more rigor than just checking the number of files
            var qFileCount = Directory.Exists(index.GetQueryIndexPath())
                ? Directory.GetFiles(index.GetQueryIndexPath()).Count()
                : 0;

            return qFileCount > 0;
        }

        private void StripContent(ref PagedResult<IndexedItem> page)
        {
            //Note: I'm not fond of this method but it works with litedb; since litedb, in our case, just returns the whole object.
            //the alternative is casting but I don't think that's needed right now
            foreach (var indexedItem in page.Results)
            {
                indexedItem.JsonContent = string.Empty;
            }
        }

        private IndexedItem MetadataToIndexedItem(FileMetadata metadata)
        {
            //Note: drive is not indexed since this index sits with-in the drive's structure
            return new IndexedItem()
            {
                FileId = metadata.File.FileId,
                SenderDotYouId = metadata.SenderDotYouId,
                CreatedTimestamp = metadata.Created,
                LastUpdatedTimestamp = metadata.Updated,
                Tags = metadata.AppData.Tags,
                FileType = metadata.AppData.FileType,
                DataType = metadata.AppData.DataType,
                ContentIsComplete = metadata.AppData.ContentIsComplete,
                JsonContent = metadata.AppData.JsonContent,
                AccessControlList = metadata.AccessControlList,
                Alias = metadata.AppData.Alias,
                PayloadIsEncrypted = metadata.PayloadIsEncrypted,
            };
        }

        private void AssertValidIndexLoaded()
        {
            if (_indexReadyState == IndexReadyState.Ready)
            {
                return;
            }

            //keep checking in-case a background indexing makes the index available
            _indexReadyState = IsValidIndex(_currentIndex) ? IndexReadyState.Ready : IndexReadyState.RequiresRebuild;
            if (_indexReadyState != IndexReadyState.Ready)
            {
                throw new NoValidIndexException(this.Drive.Id);
            }
        }
    }
}