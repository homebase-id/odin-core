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
            UnixTimeUtc? markedForDeletionDate, string email,
            bool enablePublicWebPresence = true)
        {
            this.DotYouRegistryId = dotYouRegistryId;
            this.HostOdinId = hostOdinId;
            this.TenantPathManager = tenantPathManager;
            this.FirstRunToken = firstRunToken;
            this.IsPreconfigured = isPreconfigured;
            this.MarkedForDeletionDate = markedForDeletionDate;
            this.Email = email;
            this.EnablePublicWebPresence = enablePublicWebPresence;
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
        /// Whether <see cref="Settings"/> is the shared <see cref="TenantSettings.Default"/> fallback rather
        /// than this tenant's stored configuration. True means <see cref="UpdateSystemConfig"/> never ran on
        /// <i>this instance</i> -- either the tenant was not initialized, or the settings were loaded into a
        /// different TenantContext than the one being read. Both are invisible from the setting's value
        /// alone, since the fallback silently answers with defaults.
        /// </summary>
        public bool IsUsingDefaultSettings => _tenantSettings == null;

        /// <summary>
        /// Set during the first provisioning process which allows for the bearer to set execute on-boarding steps such as setting the owner password
        /// </summary>
        public Guid? FirstRunToken { get; private set; }

        // TODO:TODD temporary measure for auto-provisioning of development domains; need a better solution"
        public bool IsPreconfigured { get; private set; }

        public UnixTimeUtc? MarkedForDeletionDate { get; private set; }

        public UnixTimeUtc? LastSeen { get; private set; }

        /// <summary>
        /// Whether this tenant is allowed a public home page (link previews, SEO/SSR content, etc.)
        /// </summary>
        public bool EnablePublicWebPresence { get; private set; } = true;

        public void Update(TenantContext source)
        {
            this.DotYouRegistryId = source.DotYouRegistryId;
            this.HostOdinId = source.HostOdinId;
            this.FirstRunToken = source.FirstRunToken;
            this.IsPreconfigured = source.IsPreconfigured;
            this.TenantPathManager = source.TenantPathManager;
            this.Email = source.Email;
            this.LastSeen = source.LastSeen;
            this.EnablePublicWebPresence = source.EnablePublicWebPresence;
        }
        
        public void UpdateSystemConfig(TenantSettings newConfig)
        {
            _tenantSettings = newConfig;
        }
    }
}