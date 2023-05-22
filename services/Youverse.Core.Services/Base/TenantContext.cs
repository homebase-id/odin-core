using System;
using System.IO;
using Youverse.Core.Identity;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Configuration;

namespace Youverse.Core.Services.Base
{
    public class TenantContext
    {
        private TenantSettings _tenantSettings;
        public Guid DotYouRegistryId { get; private set; }

        /// <summary>
        /// Specifies the OdinId of the host
        /// </summary>
        public OdinId HostOdinId { get; private set; }

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
        /// Specifies the storage locations for various pieces of data for this <see cref="HostOdinId"/>.
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
        public Guid? FirstRunToken { get; private set; }

        // TODO:TODD temporary measure for auto-provisioning of development domains; need a better solution"
        public bool IsPreconfigured { get; private set; }

        public void Update(Guid registrationId, string tenantHostName, string rootPath, CertificateRenewalConfig certificateRenewalConfig, Guid? firstRunToken, bool isPreconfigured,
            string tenantDataPayloadPath)
        {
            this.DotYouRegistryId = registrationId;
            this.HostOdinId = (OdinId)tenantHostName;

            this.CertificateRenewalConfig = certificateRenewalConfig;
            this.DataRoot = Path.Combine(rootPath, DotYouRegistryId.ToString());
            this.TempDataRoot = Path.Combine(rootPath, "temp", DotYouRegistryId.ToString());
            this.StorageConfig = new TenantStorageConfig(Path.Combine(this.DataRoot, "headers"), Path.Combine(this.TempDataRoot, "temp"), tenantDataPayloadPath);
            this.SslRoot = Path.Combine(DataRoot, "ssl");
            this.FirstRunToken = firstRunToken.GetValueOrDefault();

            this.IsPreconfigured = isPreconfigured;

            Directory.CreateDirectory(this.DataRoot);
            Directory.CreateDirectory(this.SslRoot);
            Directory.CreateDirectory(this.TempDataRoot);
            
            Directory.CreateDirectory(this.StorageConfig.DataStoragePath);
            Directory.CreateDirectory(this.StorageConfig.TempStoragePath);
            Directory.CreateDirectory(this.StorageConfig.PayloadStoragePath);
        }

        public void UpdateSystemConfig(TenantSettings newConfig)
        {
            _tenantSettings = newConfig;
        }

        public static TenantContext Create(Guid registryId, string tenantHostName, string rootPath, CertificateRenewalConfig certificateRenewalConfig, string tenantDataPayloadPath)
        {
            var tc = new TenantContext();
            tc.Update(registryId, tenantHostName, rootPath, certificateRenewalConfig, null, false, tenantDataPayloadPath);
            return tc;
        }
    }
}