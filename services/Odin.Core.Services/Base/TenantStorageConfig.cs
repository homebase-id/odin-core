namespace Odin.Core.Services.Base
{
    /// <summary>
    /// Configuration for how data is stored.  This can include paths for storing images as well as database connection strings.
    /// </summary>
    public class TenantStorageConfig
    {
        public TenantStorageConfig(string dataStoragePath, string tempStoragePath, string payloadStoragePath)
        {
            DataStoragePath = dataStoragePath;
            TempStoragePath = tempStoragePath;
            PayloadStoragePath = payloadStoragePath;
        }

        public string DataStoragePath { get; }

        public string PayloadStoragePath { get; }

        public string TempStoragePath { get; }
    }
}