using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// A circle an app creates for itself.  No id or owning app: the server picks the id, and the caller is the owner.
/// </summary>
/// <remarks>
/// Also the shape of a circle an app declares in its install request (<c>AppRegistrationRequest.Circles</c>,
/// #1939), where <see cref="Slug"/> is required and the grants are bounded by the registration rather than the
/// caller.  Declaring one does not make it reach every device: only the first install shows the install screen,
/// so an app that needs its circle everywhere still calls <c>circles/create</c> with the same slug.
/// </remarks>
public class CreateAppCircleRequest
{
    public string Name { get; set; }

    /// <summary>
    /// The circle's address within the calling app (#1939).  When the app already holds a circle with this slug,
    /// the create returns that circle's id and changes nothing -- so setup on a second device, with no local
    /// state, finds the circle the first one made.  Optional: without it every call makes a new circle, with a
    /// slug made from <see cref="Name"/>.
    /// </summary>
    public OdinSlug Slug { get; set; }

    public string Description { get; set; }

    public string Emoji { get; set; }

    /// <summary>
    /// Only access the calling app holds itself, and on a drive it does not own, only Read with the storage key.
    /// </summary>
    public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }

    /// <summary>Only permission keys the calling app holds.</summary>
    public PermissionSet Permissions { get; set; }

    /// <summary>
    /// What kind of circle it is; <see cref="CircleDesignation.Personal"/> when left out.  An app sharing a file with
    /// one person makes them an <see cref="CircleDesignation.Individual"/> circle, which grants nothing
    /// (docs/file-access.md, "Sharing with one person").
    /// </summary>
    public CircleDesignation Designation { get; set; } = CircleDesignation.Personal;

    /// <summary>
    /// Who joins it: the ones the app adds (<see cref="CircleGrantOn.None"/>, when left out), or every connection as it
    /// connects (<see cref="CircleGrantOn.Connect"/>), held to the deposit-only rule as every such circle
    /// (<c>CircleDefinitionService.AssertDepositOnlyIfAmbientAsync</c>): how a sharer reaches a member's drive to leave
    /// a marker (docs/file-access.md, "Telling someone you shared with them: markers").  Connections made before it are
    /// the app's to add (<c>enrollment-candidates</c>, <c>circles/add-many</c>).
    /// </summary>
    public CircleGrantOn GrantOn { get; set; } = CircleGrantOn.None;

    /// <summary>
    /// An app makes <see cref="CircleDesignation.Personal"/> circles, as it always has, and individual ones; the other
    /// kinds change how the owner reviews a connection (<c>CircleNetworkService.ClearReviewAsync</c>), so they stay
    /// the owner's.  It enrols by hand or on connect; enrolling through its own consent flow or at the owner's review
    /// stays with the built-in apps.
    /// </summary>
    public void AssertKind()
    {
        if (Designation is not (CircleDesignation.Personal or CircleDesignation.Individual))
        {
            throw new OdinClientException($"An app's circle is Personal or Individual, not {Designation}", OdinClientErrorCode.ArgumentError);
        }

        if (GrantOn is not (CircleGrantOn.None or CircleGrantOn.Connect))
        {
            throw new OdinClientException($"An app's circle enrols by hand or on connect, not {GrantOn}", OdinClientErrorCode.ArgumentError);
        }
    }
}
