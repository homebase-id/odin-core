using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Security;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Indexes data for a given drive an index
    /// </summary>
    public class LiteDbDriveMetadataIndexer : IDriveMetadataIndexer
    {
        private readonly ILogger<object> _logger;
        private readonly StorageDrive _storageDrive;
        
        private readonly IStorageManager _storageManager;
        private readonly IGranteeResolver _granteeResolver;
        
        public LiteDbDriveMetadataIndexer(StorageDrive storageDrive, IGranteeResolver granteeResolver, IStorageManager storageManager, ILogger<object> logger)
        {
            _storageDrive = storageDrive;
            _granteeResolver = granteeResolver;
            _storageManager = storageManager;
            _logger = logger;
        }

        public async Task Rebuild(StorageDriveIndex index)
        {
            if (Directory.Exists(index.IndexRootPath))
            {
                Directory.Delete(index.IndexRootPath, true);
            }
            
            Directory.CreateDirectory(index.IndexRootPath);


            // var fileList = _storageManager.GetFileList();
            // foreach (var file in fileList)
            // {
            //     //  read permission from the fileid.acl
            //     //      create IndexedItemPermission
            //     //  create IndexedItem
            // }
        }
    }
}