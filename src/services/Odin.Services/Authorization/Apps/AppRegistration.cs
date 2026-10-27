using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Apps
{
    public class AppRegistration
    {
        /// <summary>
        /// Promoted from the <c>AppRegistrations.AppId</c> column.
        /// </summary>
        /// <remarks>
        /// The registration lives in a table row now, not a blob.  <see cref="AppId"/>,
        /// <see cref="AppSlug"/>, <see cref="Name"/> and <see cref="CorsHostName"/> are columns and are
        /// kept out of <c>grantJson</c>, so a query on a column can never disagree with the hydrated
        /// object.  Everything else still rides the JSON.
        /// </remarks>
        [JsonIgnore]
        public GuidId AppId { get; set; }

        /// <summary>
        /// The app's wire address -- the segment other identities use to resolve it
        /// (<c>/apps/{appSlug}/drives/{driveSlug}</c>).  Immutable once written, and unique per identity.
        /// </summary>
        [JsonIgnore]
        public OdinSlug AppSlug { get; set; }

        [JsonIgnore]
        public string Name { get; set; }

        /// <summary>
        /// List of circles defining whose members can work with your identity via this app
        /// </summary>
        public List<Guid> AuthorizedCircles { get; set; }
        
        /// <summary>
        /// Permissions granted to members of the <see cref="AuthorizedCircles"/>
        /// </summary>
        public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }
        
        /// <summary>
        /// Permissions and drives granted to this app and only this app as used by the Identity Owner
        /// </summary>
        [JsonPropertyName("grant")]
        public KeyStore AppKeyStore { get; set; }

        [JsonIgnore]
        public string CorsHostName { get; set; }

        /// <summary>
        /// The app's zero-knowledge key (AppZkK), locked by the browser with the zero-knowledge master key:
        /// only the owner console opens it.  Null until the browser first makes one
        /// (docs/zero-knowledge-drive-api-plan.md, step 3).
        /// </summary>
        public string ZkMasterKeyEncryptedAppKey { get; set; }

        /// <summary>
        /// The key of each zero-knowledge drive the app is granted, by drive id, locked by the browser with
        /// AppZkK: what the app's devices open.  Kept here rather than on the grant, which the server re-mints
        /// without the browser (<see cref="AppRegistrationService.UpdateAppPermissionsAsync"/>).
        /// </summary>
        public Dictionary<Guid, string> ZkAppKeyEncryptedStorageKeys { get; set; }

        public RedactedAppRegistration Redacted()
        {
            //NOTE: we're not sharing the encrypted app dek, this is crucial
            return new RedactedAppRegistration()
            {
                AppId = this.AppId,
                AppSlug = this.AppSlug,
                Name = this.Name,
                IsRevoked = this.AppKeyStore.IsRevoked,
                IsBuiltIn = AppUninstallService.IsBuiltIn(this.AppId),
                Created = this.AppKeyStore.Created,
                AuthorizedCircles = this.AuthorizedCircles,
                CircleMemberPermissionSetGrantRequest = this.CircleMemberPermissionGrant ?? new PermissionSetGrantRequest(),
                Modified = this.AppKeyStore.Modified,
                CorsHostName = this.CorsHostName,
                Grant = this.AppKeyStore.Redacted()
            };
        }
    }
}