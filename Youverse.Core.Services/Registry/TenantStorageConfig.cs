namespace Youverse.Core.Services.Registry
{
    /// <summary>
    /// Configuration for how data is stored.  This can include paths for storing images as well as database connection strings.
    /// </summary>
    public class TenantStorageConfig
    {
        private readonly string _dataStoragePath;

        public TenantStorageConfig(string dataStoragePath)
        {
            _dataStoragePath = dataStoragePath;
        }

        public string DataStoragePath { get => this._dataStoragePath; }
    }
}
