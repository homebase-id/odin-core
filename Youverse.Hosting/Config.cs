using Youverse.Core.Util;

namespace Youverse.Hosting
{
    //Instantiated by configuration
    public class Config
    {
        private string _tenantDataRootPath;
        private string _tempTenantDataRootPath;
        private string _logFilePath;

        /// <summary>
        /// Specifies the endpoint of the registry server
        /// </summary>
        public string RegistryServerUri { get; set; }

        /// <summary>
        /// Specifies the root path where permanent tenant data is stored.
        /// </summary>
        public string TenantDataRootPath
        {
            get => _tenantDataRootPath;
            set => _tenantDataRootPath = PathUtil.OsIfy(value);
        }
        
        /// <summary>
        /// Specifies the root path where permanent tenant data is stored.
        /// </summary>
        public string TempTenantDataRootPath
        {
            get => _tempTenantDataRootPath;
            set => _tempTenantDataRootPath = PathUtil.OsIfy(value);
        }

        public string LogFilePath
        {
            get => _logFilePath;
            set => _logFilePath = PathUtil.OsIfy(value);
        }
    }
}