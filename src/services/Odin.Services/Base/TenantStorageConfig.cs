using System;
using System.IO;

namespace Odin.Services.Base
{
    /// <summary>
    /// Configuration for how data is stored.  This can include paths for storing images as well as database connection strings.
    /// </summary>
    public class TenantStorageConfig(
        string headerDataStoragePath,
        string tempStoragePath,
        string payloadStoragePath,
        string staticFileStoragePath,
        string payloadShardKey)
    {
        public string PayloadShardKey { get; } = payloadShardKey;
        public string HeaderDataStoragePath { get; } = headerDataStoragePath;
        public string PayloadStoragePath { get; } = payloadStoragePath;
        public string TempStoragePath { get; } = tempStoragePath;
        public string StaticFileStoragePath { get; } = staticFileStoragePath;


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