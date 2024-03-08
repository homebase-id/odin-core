namespace Odin.Services.Base
{
    /// <summary>
    /// Configuration for how data is stored.  This can include paths for storing images as well as database connection strings.
    /// </summary>
    public class TenantStorageConfig
    {
        public TenantStorageConfig(string headerDataStoragePath, string tempStoragePath, string payloadStoragePath, string staticFileStoragePath)
        {
            HeaderDataStoragePath = headerDataStoragePath;
            TempStoragePath = tempStoragePath;
            PayloadStoragePath = payloadStoragePath;
            StaticFileStoragePath = staticFileStoragePath;
        }

        public string HeaderDataStoragePath { get; }

        public string PayloadStoragePath { get; }

        public string TempStoragePath { get; }
        
        /// <summary>
        /// The root path for static files
        /// </summary>
        public string StaticFileStoragePath { get; }

    }
}