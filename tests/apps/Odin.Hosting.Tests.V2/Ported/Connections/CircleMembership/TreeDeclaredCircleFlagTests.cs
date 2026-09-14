#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// <c>CircleDefinition.IsTreeDeclared</c> tells a client whether editing a circle's owner, grant rule or
/// designation will survive the next version upgrade.
/// </summary>
/// <remarks>
/// The flag exists because owning an app and being declared by one are different things, and only the
/// second is re-applied by <c>ApplyTreeDefinitionAsync</c>.  An app mints circles at runtime too -- one
/// per feed channel, say -- and those are named in no catalogue, so an edit to one sticks.  A client that
/// keys "don't offer to edit this" on <c>AppId</c> therefore hides the control from nearly every circle
/// that has one, which is what the owner console did before this flag existed.
/// <para>
/// Derived on read rather than stored, so these also pin that it cannot be written: a client that echoes
/// a fetched definition back must not be able to talk a circle into or out of the catalogue.
/// </para>
/// </remarks>
[TestFixture]
public class TreeDeclaredCircleFlagTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    [Test]
    public async Task ACircleTheTreeDeclaresIsMarked()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var moments = await frodo.Admin.GetCircleDefinition(BuiltinCircles.MomentsCircle.Id);

        Assert.That(moments.Content!.IsTreeDeclared, Is.True,
            "Moments is declared in the app tree, so its rule is re-applied on every upgrade");
    }

    [Test]
    public async Task AnAppsRuntimeCircleIsNotMarked()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (circleId, _) = await CreateAppOwnedCircleAsync(frodo);

        var definition = await frodo.Admin.GetCircleDefinition(circleId);

        // The point of the flag: app-owned, but declared nowhere, so an owner's edit stands.
        Assert.That(definition.Content!.AppId, Is.Not.Null);
        Assert.That(definition.Content!.IsTreeDeclared, Is.False,
            "a circle minted at runtime is owned by an app but named by no catalogue");
    }

    [Test]
    public async Task TheFlagCannotBeWritten()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (circleId, _) = await CreateAppOwnedCircleAsync(frodo);

        var definition = (await frodo.Admin.GetCircleDefinition(circleId)).Content!;
        definition.IsTreeDeclared = true;
        var update = await frodo.Admin.TryUpdateCircleDefinition(definition);
        Assert.That(update.IsSuccessStatusCode, Is.True, "the field is ignored, not rejected");

        var reread = await frodo.Admin.GetCircleDefinition(circleId);

        Assert.That(reread.Content!.IsTreeDeclared, Is.False,
            "the answer comes from the build's catalogue, never from the row");
    }

    /// <summary>A circle owned by a real app, created the way an app creates one at runtime.</summary>
    private static async Task<(Guid circleId, Guid appId)> CreateAppOwnedCircleAsync(OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "appDrive", allowAnonymousReads: false);

        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Write | DrivePermission.React,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var grant = new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = drive,
                        Permission = DrivePermission.Write | DrivePermission.React
                    }
                }
            },
            PermissionSet = new PermissionSet(new List<int>())
        };

        var circleId = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleId, "runtime-minted", grant, appId: app.AppId);

        return (circleId, app.AppId);
    }
}
