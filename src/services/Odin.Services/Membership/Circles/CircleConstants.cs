using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Services.Membership.Circles;

public static class SystemCircleConstants
{
    public static readonly GuidId ConfirmedConnectionsCircleId = Guid.Parse("bb2683fa402aff866e771a6495765a15");
    public static readonly GuidId AutoConnectionsCircleId = Guid.Parse("9e22b42952f74d2580e11250b651d343");

    /// <summary>
    /// Carries the grants a contact should hold only once the owner has reviewed them.
    /// </summary>
    /// <remarks>
    /// The successor to the Confirmed Connections circle, and deliberately not a rename of it: that id
    /// means "every connection I confirmed" in stored grants, in tests and in the clients, and this one
    /// means something narrower.
    /// <para>
    /// A system circle rather than a built-in one.  Membership is a consequence of the review and is
    /// managed for the owner, not a list they curate -- and for a security primitive, being reconciled
    /// against a constant is the point.
    /// </para>
    /// </remarks>
    public static readonly GuidId ReviewedConnectionsCircleId = Guid.Parse("c17a2000-0000-4000-8000-000000000001");

    public static bool IsSystemCircle(Guid circleId)
    {
        return AllSystemCircles.Exists(c => c == circleId);
    }

    public static readonly List<GuidId> AllSystemCircles =
    [
        ConfirmedConnectionsCircleId,
        AutoConnectionsCircleId,
        ReviewedConnectionsCircleId
    ];

    /// <summary>
    /// A contact joins this when the owner reviews them, and leaves it when the owner un-reviews them.
    /// </summary>
    /// <remarks>
    /// One grant, on purpose.  Everything else the Confirmed Connections circle handed out is now carried
    /// by the apps' own grant-on-connect circles, which reach a contact the moment they connect.  What is
    /// left is the shard drive: a contact writes their recovery shards into it, and only someone the owner
    /// has actually looked at should be able to.  An ambient circle could hold this grant -- it is
    /// write-only, so the deposit-only rule permits it -- but it would reach every connection, which is
    /// broader than the rule being expressed.
    /// <para>
    /// <see cref="CircleGrantOn.Review"/> is what makes the timing right, and this is the first circle to
    /// use it.  Read grants and permission keys are legal here and nowhere else, so this is where later
    /// "you get this once I know who you are" grants belong.
    /// </para>
    /// </remarks>
    public static readonly CircleDefinition ReviewedConnectionsDefinition = new()
    {
        Id = ReviewedConnectionsCircleId.Value,
        Name = "Reviewed Connections",
        Description = "Contains identities you have reviewed, and carries the grants that only a reviewed connection should hold",
        GrantOn = CircleGrantOn.Review,
        DriveGrants =
        [
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ShardRecoveryDrive,
                    Permission = DrivePermission.Write
                }
            }
        ],
        Permissions = new PermissionSet()
    };

    public static readonly CircleDefinition ConfirmedConnectionsDefinition = new()
    {
        Id = ConfirmedConnectionsCircleId.Value,
        Name = "Confirmed Connected Identities",
        Description =
            "Contains identities which you have confirmed as a connection, either by approving the connection yourself or upgrading an introduced connection",
        DriveGrants =
        [
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ChatDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ListsDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MomentsDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MailDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.FeedDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ShardRecoveryDrive,
                    Permission = DrivePermission.Write
                }
            },
        ],
        Permissions = new PermissionSet()
        {
            Keys = [PermissionKeys.AllowIntroductions]
        }
    };

    public static readonly CircleDefinition AutoConnectionsSystemCircleDefinition = new()
    {
        Id = SystemCircleConstants.AutoConnectionsCircleId.Value,
        Name = "Auto-connected Identities",
        Description = "Contains all identities which were automatically connected (due to an introduction from another-connected identity)",
        DriveGrants =
        [
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ChatDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ListsDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MomentsDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MailDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.FeedDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            }
        ],
        Permissions = new PermissionSet()
        {
            Keys = []
        }
    };
}