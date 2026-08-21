#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Authorization.Apps
{
    /// <summary>
    /// A circle an app wants to exist, declared at registration.
    /// </summary>
    /// <remarks>
    /// This is the direct successor of <c>AuthorizedCircles</c> + <c>CircleMemberPermissionGrant</c>,
    /// which is how clients currently name the two hardcoded system circles. It belongs in the
    /// registration payload rather than a later call for two reasons: a <see cref="CircleGrantOn.Connect"/>
    /// circle grants ambiently, so the first connection after install must already know about it; and the
    /// install consent screen is where the owner agrees to it.
    /// <para>
    /// <see cref="Id"/> is supplied by the app and is what an update matches on, so the same registration
    /// replayed updates its circles rather than duplicating them.
    /// </para>
    /// </remarks>
    public class AppDefaultCircleRequest
    {
        /// <summary>
        /// Stable id, chosen by the app.  Update matches on this.
        /// </summary>
        public Guid Id { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        /// <summary>
        /// Drives granted to members.  Bound by the deposit-only invariant when
        /// <see cref="GrantOn"/> enrols ambiently.
        /// </summary>
        public IEnumerable<DriveGrantRequest>? DriveGrants { get; set; }

        /// <summary>
        /// Identity-wide permission keys granted to members.  Only ever allowed on a
        /// <see cref="CircleGrantOn.Review"/> circle -- the review is the key ceremony.
        /// </summary>
        public PermissionSet? Permissions { get; set; }

        /// <summary>
        /// When members are enrolled.  See <see cref="CircleGrantOn"/>.
        /// </summary>
        public CircleGrantOn GrantOn { get; set; } = CircleGrantOn.None;

        /// <summary>
        /// What kind of relationship this circle represents.  Presentation only.
        /// </summary>
        public CircleDesignation Designation { get; set; } = CircleDesignation.Personal;

        /// <summary>
        /// Optional emoji the owning app presets for this circle.
        /// </summary>
        public string? Emoji { get; set; }

        public bool IsValid()
        {
            if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name))
            {
                return false;
            }

            if (!Enum.IsDefined(GrantOn) || !Enum.IsDefined(Designation))
            {
                return false;
            }

            return DriveGrants == null || DriveGrants.All(dgr => dgr.PermissionedDrive.Drive.IsValid());
        }

        public CreateCircleRequest ToCreateCircleRequest(Guid appId)
        {
            return new CreateCircleRequest
            {
                Id = Id,
                Name = Name,
                Description = Description,
                DriveGrants = DriveGrants,
                Permissions = Permissions,
                AppId = appId,
                GrantOn = GrantOn,
                Designation = Designation,
                Emoji = Emoji
            };
        }
    }
}
