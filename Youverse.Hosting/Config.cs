using DotYou.Types;
using Youverse.Core.Util;

namespace DotYou.TenantHost
{
    //Instantiated by configuration
    public class Config
    {
        private string _tenantDataRootPath;
        private string _logFilePath;

        /// <summary>
        /// Specifies the endpoint of the registry server
        /// </summary>
        public string RegistryServerUri { get; set; }

        /// <summary>
        /// Specifies the root path where tenant data is stored.
        /// </summary>
        public string TenantDataRootPath
        {
            get => _tenantDataRootPath;
            set => _tenantDataRootPath = PathUtil.OsIfy(value);
        }

        public string LogFilePath
        {
            get => _logFilePath;
            set => _logFilePath = PathUtil.OsIfy(value);
        }
    }
}