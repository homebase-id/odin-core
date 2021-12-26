using System;
using System.Diagnostics;
using System.IO;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    [DebuggerDisplay("Tier={Tier}, Path={IndexRootPath}")]
    public sealed class StorageDriveIndex
    {
        public IndexTier Tier { get; }

        public StorageDriveIndex(IndexTier tier, string rootPath)
        {
            Tier = tier;
            string folder = Tier == IndexTier.Primary ? "p" : "s";
            IndexRootPath = Path.Combine(rootPath, folder);
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