namespace Youverse.Core.Services.Registry
{
    /// <summary>
    /// Configuration for how data is stored.  This can include paths for storing images as well as database connection strings.
    /// </summary>
    public class TenantStorageConfig
    {
        private readonly string _dataStoragePath;
        private readonly string _tempStoragePath;

        public TenantStorageConfig(string dataStoragePath, string tempStoragePath)
        {
            _dataStoragePath = dataStoragePath;
            _tempStoragePath = tempStoragePath;
        }

        public string DataStoragePath
        {
            get => this._dataStoragePath;
        }

        public string TempStoragePath
        {
            get => this._tempStoragePath;
        }
    }
}