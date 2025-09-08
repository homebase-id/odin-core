using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Base
{
    public class TenantContext
    {
        private TenantSettings _tenantSettings;

        public TenantContext()
        {
        }

        public SensitiveByteArray TemporalEncryptionKey { get; } = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        
        public TenantContext(Guid dotYouRegistryId,
            OdinId hostOdinId,
            TenantPathManager tenantPathManager,
            Guid? firstRunToken,
            bool isPreconfigured,
            UnixTimeUtc? markedForDeletionDate, string email)
        {
            this.DotYouRegistryId = dotYouRegistryId;
            this.HostOdinId = hostOdinId;
            this.TenantPathManager = tenantPathManager;
            this.FirstRunToken = firstRunToken;
            this.IsPreconfigured = isPreconfigured;
            this.MarkedForDeletionDate = markedForDeletionDate;
            this.Email = email;
        }

        public string Email { get; private set; }

        public Guid DotYouRegistryId { get; private set; }

        /// <summary>
        /// Specifies the OdinId of the host
        /// </summary>
        public OdinId HostOdinId { get; private set; }

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="HostOdinId"/>.
        /// </summary>
        public TenantPathManager TenantPathManager { get; private set; }

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

        public UnixTimeUtc? LastSeen { get; private set; }

        public void Update(TenantContext source)
        {
            this.DotYouRegistryId = source.DotYouRegistryId;
            this.HostOdinId = source.HostOdinId;
            this.FirstRunToken = source.FirstRunToken;
            this.IsPreconfigured = source.IsPreconfigured;
            this.TenantPathManager = source.TenantPathManager;
            this.Email = source.Email;
            this.LastSeen = source.LastSeen;
        }
        
        public void UpdateSystemConfig(TenantSettings newConfig)
        {
            _tenantSettings = newConfig;
        }
    }
}