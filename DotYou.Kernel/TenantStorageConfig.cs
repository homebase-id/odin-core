namespace DotYou.Kernel
{
    /// <summary>
    /// Configuration for how data is stored.  This can include paths for storing images as well as database connection strings.
    /// </summary>
    public class TenantStorageConfig
    {
        private readonly string _dataStoragePath;
        private readonly string _imageStoragePath;

        public TenantStorageConfig(string dataStoragePath, string imageStoragePath)
        {
            _dataStoragePath = dataStoragePath;
            _imageStoragePath = imageStoragePath;
        }

        public string DataStoragePath { get => this._dataStoragePath; }

        public string ImageStoragePath { get => this._imageStoragePath; }
    }
}
