using System;
using System.IO;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public sealed class StorageDriveIndex
    {
        public IndexTier IndexTier { get; }

        public StorageDriveIndex(IndexTier indexIndexTier, string rootPath)
        {
            IndexTier = indexIndexTier;
            string folder = IndexTier == IndexTier.Primary ? "p" : "s";
            IndexRootPath = Path.Combine(rootPath, folder, "_idx");
        }

        public string QueryIndexName => "dq_idx";

        public string PermissionIndexName => "dp_idx";

        public string IndexRootPath { get; }

        public string GetQueryIndexPath()
        {
            return Path.Combine(this.IndexRootPath, this.QueryIndexName);
        }

        public string GetPermissionIndexPath()
        {
            return Path.Combine(this.IndexRootPath, this.PermissionIndexName);
        }
    }
}