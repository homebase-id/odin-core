using System;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Tests.Membership.Circles;

/// <summary>
/// <see cref="CircleDefinition"/> is both the stored shape and the wire shape: the circle definition
/// API serves it and takes it as an update body.  The four promoted fields have to be excluded from
/// the row's data blob without being hidden from JSON, and those two requirements pull in opposite
/// directions.
/// </summary>
[TestFixture]
public class CircleDefinitionStorageTests
{
    [Test]
    public void ToRecordKeepsThePromotedValuesOutOfTheBlob()
    {
        var definition = Sample();

        var record = CircleDefinitionService.ToRecord(definition);
        var blob = record.data.ToStringFromUtf8Bytes();

        // The keys are still written -- they are ordinary properties now, because the type is also the
        // wire shape. What must not survive is any *value*: a stale copy in the blob is what would let a
        // query on the column disagree with the hydrated object. Deserializing the blob alone yields
        // defaults, so there is nothing to drift from.
        var fromBlobAlone = OdinSystemSerializer.Deserialize<CircleDefinition>(blob)!;

        Assert.That(fromBlobAlone.AppId, Is.Null, "AppId belongs to the column, not the blob");
        Assert.That(fromBlobAlone.GrantOn, Is.EqualTo(CircleGrantOn.None), "GrantOn belongs to the column");
        Assert.That(fromBlobAlone.Designation, Is.EqualTo(CircleDesignation.Personal));
        Assert.That(fromBlobAlone.Emoji, Is.Null, "Emoji belongs to the column, not the blob");

        Assert.That(blob, Does.Not.Contain("\uD83C\uDF7B").And.Not.Contain("🍻"),
            "the emoji value must not be in the blob");

        // ...while the columns carry them.
        Assert.That(record.AppId, Is.EqualTo(definition.AppId));
        Assert.That(record.GrantOn, Is.EqualTo((int)CircleGrantOn.Connect));
        Assert.That(record.Designation, Is.EqualTo((int)CircleDesignation.Vendor));
        Assert.That(record.Emoji, Is.EqualTo("🍻"));
    }

    [Test]
    public void ToRecordLeavesTheCallersObjectIntact()
    {
        // The fields are cleared to serialize and restored afterwards; the caller is still holding it.
        var definition = Sample();

        CircleDefinitionService.ToRecord(definition);

        Assert.That(definition.AppId, Is.Not.Null);
        Assert.That(definition.GrantOn, Is.EqualTo(CircleGrantOn.Connect));
        Assert.That(definition.Designation, Is.EqualTo(CircleDesignation.Vendor));
        Assert.That(definition.Emoji, Is.EqualTo("🍻"));
    }

    [Test]
    public void ThePromotedFieldsSurviveARoundTripThroughTheRecord()
    {
        var definition = Sample();

        var restored = CircleDefinitionService.FromRecord(CircleDefinitionService.ToRecord(definition));

        Assert.That(restored.AppId, Is.EqualTo(definition.AppId));
        Assert.That(restored.GrantOn, Is.EqualTo(definition.GrantOn));
        Assert.That(restored.Designation, Is.EqualTo(definition.Designation));
        Assert.That(restored.Emoji, Is.EqualTo(definition.Emoji));
        Assert.That(restored.Name, Is.EqualTo(definition.Name));
    }

    [Test]
    public void ThePromotedFieldsAreVisibleOnTheWire()
    {
        // The definition API serves this type directly. If these were [JsonIgnore] the client would
        // never see them -- and, worse, an update body would arrive with GrantOn defaulted, silently
        // resetting a circle from Connect back to None.
        var json = OdinSystemSerializer.Serialize(Sample());

        Assert.That(json, Does.Contain("appId"));
        Assert.That(json, Does.Contain("grantOn"));
        Assert.That(json, Does.Contain("designation"));
        Assert.That(json, Does.Contain("emoji"));
    }

    [Test]
    public void AnUpdateBodyRoundTripsItsGrantOn()
    {
        // The regression this guards: serialize a circle, send it back as an update, and it must still
        // say what it said.
        var sent = OdinSystemSerializer.Serialize(Sample());
        var received = OdinSystemSerializer.Deserialize<CircleDefinition>(sent)!;

        Assert.That(received.GrantOn, Is.EqualTo(CircleGrantOn.Connect),
            "a client echoing a definition back must not silently reset GrantOn");
        Assert.That(received.Designation, Is.EqualTo(CircleDesignation.Vendor));
        Assert.That(received.Emoji, Is.EqualTo("🍻"));
    }


    /// <summary>
    /// The deposit-only invariant is what makes "a circle that enrols without the owner present hands
    /// out no keys" enforced rather than conventional.  Nothing sets <see cref="CircleGrantOn"/> yet, so
    /// these pin the guard before there is a caller that can trip it.
    /// </summary>
    [Test]
    public void AmbientCircleCannotCarryPermissionKeys()
    {
        var circle = Sample();
        circle.GrantOn = CircleGrantOn.Connect;
        circle.Permissions = new Services.Authorization.Permissions.PermissionSet(
            PermissionKeys.ReadConnections);

        // No drive grants, so the guard never reaches the drive manager.
        var service = new CircleDefinitionService(null, null, null);

        var ex = Assert.ThrowsAsync<OdinClientException>(
            async () => await service.AssertDepositOnlyIfAmbientAsync(circle));

        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.CannotGrantKeysOnAmbientCircle));
    }

    [Test]
    public void NonAmbientCircleIsUnaffectedByTheInvariant()
    {
        var circle = Sample();
        circle.GrantOn = CircleGrantOn.None;
        circle.Permissions = new Services.Authorization.Permissions.PermissionSet(
            PermissionKeys.ReadConnections);

        var service = new CircleDefinitionService(null, null, null);

        // Manual-membership circles are the owner's own act, so they may carry whatever they carry
        // today. This is the case every existing circle is in.
        Assert.DoesNotThrowAsync(async () => await service.AssertDepositOnlyIfAmbientAsync(circle));
    }

    private static CircleDefinition Sample()
    {
        return new CircleDefinition
        {
            Id = GuidId.FromString("beer-drinking-buddies"),
            Name = "Beer Drinking Buddies",
            Description = "a circle",
            AppId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            GrantOn = CircleGrantOn.Connect,
            Designation = CircleDesignation.Vendor,
            Emoji = "🍻",
            DriveGrants = [],
            Permissions = new Services.Authorization.Permissions.PermissionSet()
        };
    }
}
