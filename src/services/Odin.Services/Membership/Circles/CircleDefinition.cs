using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Services.Membership.Circles
{
    public class CircleDefinition : IEquatable<CircleDefinition>
    {
        public GuidId Id { get; set; }

        public UnixTimeUtc Created { get; set; }

        public UnixTimeUtc LastUpdated { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool Disabled { get; set; }

        /// <summary>
        /// The app that owns this circle; null means an owner circle.  An app may create, modify and
        /// delete only its own circles.
        /// </summary>
        /// <remarks>
        /// Promoted from the <c>Circle.AppId</c> column, along with <see cref="GrantOn"/>,
        /// <see cref="Designation"/> and <see cref="Emoji"/>.  The column is the only at-rest home:
        /// <c>CircleDefinitionService.ToRecord</c> clears all four before serializing the blob, because a
        /// second copy in there would let a query on the column disagree with the hydrated object.
        /// <para>
        /// Deliberately not <c>[JsonIgnore]</c>.  This same type is the wire shape -- the circle
        /// definition API serves it and accepts it as an update body -- so hiding the fields from JSON
        /// would both starve clients and make every client-side update reset them to their defaults.
        /// One attribute cannot make one type be both the wire shape and the stored shape; excluding
        /// them from the blob is done at the single place that writes the blob instead.
        /// </para>
        /// </remarks>
        public Guid? AppId { get; set; }

        /// <summary>
        /// The circle's address within its owning app: unique per app, <c>OdinSlug</c> rules, immutable
        /// once written (#1939).
        /// </summary>
        /// <remarks>
        /// Stored only in the <c>Circle.CircleSlug</c> column and kept out of the blob, like
        /// <see cref="AppId"/>, and not <c>[JsonIgnore]</c> for the same reason.  Set when the circle is
        /// created -- by the caller, or made from the name -- and never taken from an update.  Null only for
        /// a circle still in the pre-v13 blob store, which has no column.
        /// </remarks>
        public OdinSlug Slug { get; set; }

        /// <summary>
        /// True when the app tree declares this circle, so its owner, grant rule and designation are
        /// re-applied on every version upgrade.
        /// </summary>
        /// <remarks>
        /// Derived at read time from <see cref="Apps.Builtin.BuiltinApps.IsTreeDeclaredCircle"/>, never
        /// stored -- <c>CircleDefinitionService.ToRecord</c> clears it along with the promoted columns,
        /// for a related reason: the tree changes with the build, not with the row, so a copy at rest
        /// could disagree with the catalogue it came from.
        /// <para>
        /// A client needs this before offering to edit any of those three.  Editing a declared circle is
        /// undone by the next upgrade without saying so, whereas an app's runtime circle -- the feed app
        /// minting one per channel, say -- is owned by an app but named by nobody and stays as the owner
        /// leaves it.  <see cref="AppId"/> alone does not separate the two.
        /// </para>
        /// </remarks>
        public bool IsTreeDeclared { get; set; }

        /// <summary>
        /// When the owning app wants members enrolled.  See <see cref="CircleGrantOn"/>.
        /// </summary>
        public CircleGrantOn GrantOn { get; set; } = CircleGrantOn.None;

        /// <summary>
        /// What kind of relationship this circle represents.  Presentation only.
        /// </summary>
        public CircleDesignation Designation { get; set; } = CircleDesignation.Personal;

        /// <summary>
        /// Optional user-chosen emoji.  Stored as the full string -- these are frequently multi-codepoint
        /// ZWJ sequences and must never be substringed.
        /// </summary>
        public string Emoji { get; set; }

        /// <summary>
        /// The drives granted to members of this Circle
        /// </summary>
        public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }

        /// <summary>
        /// The permissions to be granted to members of this Circle
        /// </summary>
        public PermissionSet Permissions { get; set; }

        public bool Equals(CircleDefinition other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Id, other.Id) && Created == other.Created && LastUpdated == other.LastUpdated && Name == other.Name &&
                   Description == other.Description && Disabled == other.Disabled && MatchDriveGrants(other.DriveGrants.ToList()) &&
                   Equals(Permissions, other.Permissions) &&
                   Nullable.Equals(AppId, other.AppId) && Slug == other.Slug && GrantOn == other.GrantOn &&
                   Designation == other.Designation && Emoji == other.Emoji;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((CircleDefinition)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Created, LastUpdated, Name, Description, Disabled, DriveGrants, Permissions) ^
                   HashCode.Combine(AppId, Slug, GrantOn, Designation, Emoji);
        }
        
        private bool MatchDriveGrants(List<DriveGrantRequest> otherDriveGrants)
        {
            return !DriveGrants.Except(otherDriveGrants).Any() &&
                   !otherDriveGrants.Except(DriveGrants).Any();
        }

        /// <summary>Client-safe view: the permission set is redacted (keys only).</summary>
        public RedactedCircleDefinition Redacted()
        {
            return new RedactedCircleDefinition
            {
                Id = Id,
                Created = Created,
                LastUpdated = LastUpdated,
                Name = Name,
                Description = Description,
                Disabled = Disabled,
                AppId = AppId,
                Slug = Slug,
                IsTreeDeclared = IsTreeDeclared,
                GrantOn = GrantOn,
                Designation = Designation,
                Emoji = Emoji,
                DriveGrants = DriveGrants,
                Permissions = Permissions?.Redacted()
            };
        }
    }

    public class RedactedCircleDefinition
    {
        public GuidId Id { get; set; }
        public UnixTimeUtc Created { get; set; }
        public UnixTimeUtc LastUpdated { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Disabled { get; set; }

        /// <summary>The app that owns this circle; null means an owner circle.</summary>
        public Guid? AppId { get; set; }

        /// <summary>The circle's address within its owning app; unique per app, immutable.</summary>
        public OdinSlug Slug { get; set; }

        /// <summary>
        /// True when the app tree declares this circle, so the next version upgrade re-applies its owner,
        /// grant rule and designation -- and silently undoes any edit to them.  Derived, never stored.
        /// </summary>
        public bool IsTreeDeclared { get; set; }

        /// <summary>When the owning app wants members enrolled.</summary>
        public CircleGrantOn GrantOn { get; set; }

        /// <summary>What kind of relationship this circle represents.  Drives client presentation.</summary>
        public CircleDesignation Designation { get; set; }

        /// <summary>Optional user-chosen emoji; may be a multi-codepoint ZWJ sequence.</summary>
        public string Emoji { get; set; }

        public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }
        public RedactedPermissionSet Permissions { get; set; }
    }
}