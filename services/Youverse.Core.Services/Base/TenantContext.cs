using System;
using System.IO;
using Youverse.Core.Identity;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Services.Configuration;

namespace Youverse.Core.Services.Base
{
    public class TenantContext
    {
        private TenantSettings _tenantSettings;
        public Guid DotYouRegistryId { get; private set; }

        /// <summary>
        /// Specifies the DotYouId of the host
        /// </summary>
        public DotYouIdentity HostDotYouId { get; private set; }

        /// <summary>
        /// The root path for data
        /// </summary>
        public string DataRoot { get; private set; }

        /// <summary>
        /// The root path for temp data
        /// </summary>
        public string TempDataRoot { get; private set; }
        
        public string SslRoot { get; private set; }

        /// <summary>
        /// The root path for static files
        /// </summary>
        public string StaticFileDataRoot => Path.Combine(this.TempDataRoot, "static");

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="HostDotYouId"/>.
        /// </summary>
        public TenantStorageConfig StorageConfig { get; private set; }

        /// <summary>
        /// Configuration set by the tenant indicating various settings
        /// </summary>
        public TenantSettings Settings => _tenantSettings ?? TenantSettings.Default;

        public CertificateRenewalConfig CertificateRenewalConfig { get; set; }

        /// <summary>
        /// Set during the first provisioning process which allows for the bearer to set execute on-boarding steps such as setting the owner password
        /// </summary>
        public Guid? FirstRunToken { get; set; }

        public void Update(Guid registrationId, string tenantHostName, string rootPath, CertificateRenewalConfig certificateRenewalConfig)
        {
            this.DotYouRegistryId = registrationId;
            this.HostDotYouId = (DotYouIdentity)tenantHostName;

            this.CertificateRenewalConfig = certificateRenewalConfig;
            this.DataRoot = Path.Combine(rootPath, DotYouRegistryId.ToString());
            this.TempDataRoot = Path.Combine(rootPath, "temp", DotYouRegistryId.ToString());
            this.StorageConfig = new TenantStorageConfig(Path.Combine(this.DataRoot, "data"), Path.Combine(this.TempDataRoot, "temp"));
            this.SslRoot = Path.Combine(DataRoot, "ssl");

            Directory.CreateDirectory(this.DataRoot);
            Directory.CreateDirectory(this.SslRoot);
            Directory.CreateDirectory(this.TempDataRoot);

        }
        
        public void UpdateSystemConfig(TenantSettings newConfig)
        {
            _tenantSettings = newConfig;
        }

        public static TenantContext Create(Guid registryId, string tenantHostName, string rootPath, CertificateRenewalConfig certificateRenewalConfig)
        {
            var tc = new TenantContext();
            tc.Update(registryId, tenantHostName, rootPath,certificateRenewalConfig);
            return tc;
        }
    }
}