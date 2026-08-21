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
        public string AppSlug { get; set; }

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
        /// The default circles this app declared at registration.
        /// </summary>
        /// <remarks>
        /// Stored on the row's <c>detailsJson</c>, and used for exactly two things: showing the owner
        /// what they consented to, and re-creating the circle rows if a repair or migration needs to.
        /// It is never read on the hot path -- the rows in the Circle table are the truth.
        /// </remarks>
        [JsonIgnore]
        public List<AppDefaultCircleRequest> DefaultCircles { get; set; }

        public RedactedAppRegistration Redacted()
        {
            //NOTE: we're not sharing the encrypted app dek, this is crucial
            return new RedactedAppRegistration()
            {
                AppId = this.AppId,
                AppSlug = this.AppSlug,
                Name = this.Name,
                IsRevoked = this.AppKeyStore.IsRevoked,
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