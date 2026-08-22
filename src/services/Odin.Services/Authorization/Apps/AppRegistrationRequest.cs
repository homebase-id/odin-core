using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Apps
{
    public class AppRegistrationRequest
    {
        public GuidId AppId { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The host name used for CORS to allow the app to access the identity from a browser
        /// </summary>
        public string CorsHostName { get; set; }

        /// <summary>
        /// Permissions to be granted to this app
        /// </summary>
        public PermissionSet PermissionSet { get; set; }

        /// <summary>
        /// The list of drives of which this app should receive access
        /// </summary>
        public List<DriveGrantRequest> Drives { get; set; }

        /// <summary>
        /// List of circles defining whose members can work with your identity via this app
        /// </summary>
        public List<Guid> AuthorizedCircles { get; set; }

        /// <summary>
        /// Permissions being granted to all members of the <see cref="AuthorizedCircles"/>
        /// </summary>
        public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }

        /// <summary>
        /// Circles this app wants to exist, and when their members are enrolled.
        /// </summary>
        /// <remarks>
        /// Declared here rather than through a later call because a grant-on-connect circle enrols
        /// ambiently -- the first connection after install must already know about it -- and because the
        /// install consent screen is where the owner agrees to it.
        /// </remarks>
        public List<AppDefaultCircleRequest> DefaultCircles { get; set; }

        public bool IsValid()
        {
            var driveGrantsValid = this.Drives == null || this.Drives.Count == 0 || this.Drives.TrueForAll(dgr => dgr.PermissionedDrive.Drive.IsValid());
            var authorizedCirclesValid = this.AuthorizedCircles == null || this.AuthorizedCircles.Count == 0 ||
                                         this.AuthorizedCircles!.TrueForAll(c => c != Guid.Empty);
            // var circleGrantRequestValid = this.CircleMemberPermissionGrant?.IsValid() ?? true;
            var corsHeaderValid = string.IsNullOrEmpty(this.CorsHostName) || AppUtil.IsValidCorsHeader(this.CorsHostName);

            var defaultCirclesValid = this.DefaultCircles == null ||
                                      (this.DefaultCircles.TrueForAll(c => c.IsValid()) &&
                                       this.DefaultCircles.Select(c => c.Id).Distinct().Count() == this.DefaultCircles.Count);

            var isValid = this.AppId != Guid.Empty &&
                          !string.IsNullOrEmpty(this.Name) &&
                          !string.IsNullOrWhiteSpace(this.Name) &&
                          driveGrantsValid &&
                          authorizedCirclesValid &&
                          // circleGrantRequestValid &&
                          defaultCirclesValid &&
                          corsHeaderValid;

            return isValid;
        }
    }

    public class UpdateAuthorizedCirclesRequest
    {
        public GuidId AppId { get; set; }

        /// <summary>
        /// List of circles defining whose members can work with your identity via this app
        /// </summary>
        public List<Guid> AuthorizedCircles { get; set; }

        /// <summary>
        /// Permissions granted to members of the <see cref="AuthorizedCircles"/>
        /// </summary>
        public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }
    }

    public class UpdateAppPermissionsRequest
    {
        public GuidId AppId { get; set; }

        /// <summary>
        /// Permissions to be granted to this app
        /// </summary>
        public PermissionSet PermissionSet { get; set; }

        /// <summary>
        /// The list of drives of which this app should receive access
        /// </summary>
        public IEnumerable<DriveGrantRequest> Drives { get; set; }
    }
}