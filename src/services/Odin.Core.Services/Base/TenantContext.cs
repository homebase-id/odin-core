using System;
using Odin.Core.Identity;
using Odin.Core.Services.Configuration;
using Odin.Core.Time;

namespace Odin.Core.Services.Base
{
    public class TenantContext
    {
        private TenantSettings _tenantSettings;

        public TenantContext()
        {
        }

        public TenantContext(Guid dotYouRegistryId, OdinId hostOdinId, string sslRoot, TenantStorageConfig storageConfig, Guid? firstRunToken,
            bool isPreconfigured, UnixTimeUtc? markedForDeletionDate)
        {
            this.DotYouRegistryId = dotYouRegistryId;
            this.HostOdinId = hostOdinId;
            this.SslRoot = sslRoot;
            this.StorageConfig = storageConfig;
            this.FirstRunToken = firstRunToken;
            this.IsPreconfigured = isPreconfigured;
            this.MarkedForDeletionDate = markedForDeletionDate;
        }

        public Guid DotYouRegistryId { get; private set; }

        /// <summary>
        /// Specifies the OdinId of the host
        /// </summary>
        public OdinId HostOdinId { get; private set; }

        public string SslRoot { get; private set; }

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="HostOdinId"/>.
        /// </summary>
        public TenantStorageConfig StorageConfig { get; private set; }

        /// <summary>
        /// Configuration set by the tenant indicating various settings
        /// </summary>
        public TenantSettings Settings => _tenantSettings ?? TenantSettings.Default;

        /// <summary>
        /// Set during the first provisioning process which allows for the bearer to set execute on-boarding steps such as setting the owner password
        /// </summary>
        public Guid? FirstRunToken { get; private set; }

        // TODO:TODD temporary measure for auto-provisioning of development domains; need a better solution"
        public bool IsPreconfigured { get; private set; }

        public UnixTimeUtc? MarkedForDeletionDate { get; private set; }

        public void Update(TenantContext source)
        {
            this.DotYouRegistryId = source.DotYouRegistryId;
            this.HostOdinId = source.HostOdinId;
            this.SslRoot = source.SslRoot;
            this.StorageConfig = source.StorageConfig;
            this.FirstRunToken = source.FirstRunToken;
            this.IsPreconfigured = source.IsPreconfigured;
        }
        
        public void UpdateSystemConfig(TenantSettings newConfig)
        {
            _tenantSettings = newConfig;
        }
    }
}