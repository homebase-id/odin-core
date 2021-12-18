using System;
using System.IO;

namespace Youverse.Core.Services.Drive
{
    public enum IndexInUse
    {
        Primary = 1,
        Secondary = 2
    }
    
    /// <summary>
    /// Information about a <see cref="StorageDrive"/>
    /// </summary>
    public sealed class StorageDriveStatus
    {
        /// <summary>
        /// The Drive Id
        /// </summary>
        public Guid Id { get; set; }
        
        /// <summary>
        /// Specifies which index is currently in use.  Required for the index rebuild process
        /// </summary>
        public IndexInUse IndexInUse { get; set; }
    }

    public sealed class StorageDriveIndex
    {

        public string IndexName { get; init; }

        public string PermissionIndexName { get; init; }
        
        public string IndexPath { get; init; }
        
        public string PermissionIndexPath { get; init; }

    }
    
    /// <summary>
    /// Information about a drive
    /// </summary>
    public sealed class StorageDrive
    {
        public Guid Id { get; init; }
        
        public string RootPath { get; init; }
        
        public StorageDriveIndex GetIndex(IndexInUse indexInUse)
        {
            var indexRootPath = Path.Combine(this.RootPath, "_idx");
            var indexName = $"d_idx";
            var permissionIndexName = $"d_prm_idx";
            
            string folder = indexInUse == IndexInUse.Primary ? "p" : "s";
            var indexPath = Path.Combine(indexRootPath,folder, indexName);
            var permissionIndexPath = Path.Combine(indexRootPath,folder, permissionIndexName);

            var index = new StorageDriveIndex()
            {
                IndexName = indexName,
                IndexPath = indexPath,
                PermissionIndexName = permissionIndexName,
                PermissionIndexPath = permissionIndexPath
            };
            
            return index;
        }
    }
}