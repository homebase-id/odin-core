using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Profile;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public interface IDriveMetadataIndexer
    {
        Task Rebuild(StorageDriveIndex index);
    }

    /// <summary>
    /// Indexes data for a given drive an index
    /// </summary>
    public class LiteDbDriveMetadataIndexer : IDriveMetadataIndexer
    {
        private readonly ILogger<object> _logger;
        private readonly StorageDrive _storageDrive;
        
        private readonly IDriveManager _driveManager;
        private readonly IGranteeResolver _granteeResolver;
        
        public LiteDbDriveMetadataIndexer(StorageDrive storageDrive, IGranteeResolver granteeResolver, IDriveManager driveManager, ILogger<object> logger)
        {
            _storageDrive = storageDrive;
            _granteeResolver = granteeResolver;
            _driveManager = driveManager;
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