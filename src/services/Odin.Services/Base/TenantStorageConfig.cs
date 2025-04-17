using System;
using System.IO;

namespace Odin.Services.Base
{
    /// <summary>
    /// Configuration for how data is stored.  This can include paths for storing images as well as database connection strings.
    /// </summary>
    public class TenantStorageConfig
    {
        public TenantStorageConfig(string headerDataStoragePath, string tempStoragePath,
            string payloadStoragePath,
            string staticFileStoragePath,
            string payloadShardKey)
        {
            HeaderDataStoragePath = headerDataStoragePath;
            TempStoragePath = tempStoragePath;
            PayloadStoragePath = payloadStoragePath;
            StaticFileStoragePath = staticFileStoragePath;
            PayloadShardKey = payloadShardKey;
        }

        public string PayloadShardKey { get; }
        public string HeaderDataStoragePath { get; }
        public string PayloadStoragePath { get; }
        public string TempStoragePath { get; }
        public string StaticFileStoragePath { get; }


        public void CreateDirectories()
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(HeaderDataStoragePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(TempStoragePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(PayloadStoragePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(StaticFileStoragePath);

            Directory.CreateDirectory(HeaderDataStoragePath);
            Directory.CreateDirectory(TempStoragePath);
            Directory.CreateDirectory(PayloadStoragePath);
            Directory.CreateDirectory(StaticFileStoragePath);
        }
    }
}