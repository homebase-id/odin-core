using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Circles;

public class CreateCircleRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// The circle's address within its owning app (#1939).  Optional: made from <see cref="Name"/> when
    /// absent.  Refused if the app already holds it.  Immutable once written.
    /// </summary>
    public OdinSlug Slug { get; set; }
    
    public string Description { get; set; }
    
    /// <summary>
    /// The drives granted to members of this Circle
    /// </summary>
    public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }

    /// <summary>
    /// The permissions to be granted to members of this Circle
    /// </summary>
    public PermissionSet Permissions { get; set; }

    /// <summary>
    /// The app that owns this circle; null means an owner circle.
    /// </summary>
    public Guid? AppId { get; set; }

    /// <summary>
    /// When members are enrolled.  Defaults to manual membership only.
    /// </summary>
    public CircleGrantOn GrantOn { get; set; } = CircleGrantOn.None;

    /// <summary>
    /// What kind of relationship this circle represents.
    /// </summary>
    public CircleDesignation Designation { get; set; } = CircleDesignation.Personal;

    /// <summary>
    /// Optional user- or app-chosen emoji.
    /// </summary>
    public string Emoji { get; set; }

    /// <summary>
    /// What an app's own circle is: a new server-minted id, owned by <paramref name="appId"/>, granted only
    /// explicitly.  The one definition both <c>circles/create</c> and install-time declarations use.
    /// </summary>
    public static CreateCircleRequest ForApp(Guid appId, CreateAppCircleRequest request) => new()
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Slug = request.Slug,
        Description = request.Description,
        Emoji = request.Emoji,
        DriveGrants = request.DriveGrants,
        Permissions = request.Permissions,
        AppId = appId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal
    };
}