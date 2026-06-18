using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Verifies that circle-definition mutations (create/update/enable/disable/delete) push a
// CircleDefinitionChanged (5003) notification to the owner's own connected sessions, so other
// devices can invalidate their cached circle definitions. Single identity: the WebSocket listener
// is simply a second session of the same owner performing the mutation.
public class CircleDefinitionNotificationTests
{
    private WebScaffold _scaffold;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    private static PermissionSetGrantRequest SimpleGrant() =>
        new() { PermissionSet = new(PermissionKeys.AllowIntroductions) };

    [Test]
    public async Task CreateCircleDefinition_PushesCreatedToOwnerSessions()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var handler = new ConnectionCircleNotificationSocketHandler();
        await handler.ConnectAsync(frodo);
        try
        {
            var circleId = Guid.NewGuid();
            var response = await frodo.Network.CreateCircle(circleId, "ws-create-circle", SimpleGrant());
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var evt = await handler.WaitForCircleDefinitionChange(
                CircleDefinitionChangeType.Created, circleId, WaitTimeout);
            ClassicAssert.IsNotNull(evt, "Expected a CircleDefinitionChanged{Created} notification");
            ClassicAssert.AreEqual(circleId, evt.CircleId);
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task UpdateCircleDefinition_PushesUpdatedToOwnerSessions()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var circleId = Guid.NewGuid();
        var createResponse = await frodo.Network.CreateCircle(circleId, "ws-update-circle", SimpleGrant());
        ClassicAssert.IsTrue(createResponse.IsSuccessStatusCode);

        var handler = new ConnectionCircleNotificationSocketHandler();
        await handler.ConnectAsync(frodo);
        try
        {
            var definition = (await frodo.Network.GetCircleDefinition(circleId)).Content;
            ClassicAssert.IsNotNull(definition);
            definition.Name = "ws-update-circle-renamed";

            var updateResponse = await frodo.Network.UpdateCircleDefinition(definition);
            ClassicAssert.IsTrue(updateResponse.IsSuccessStatusCode);

            var evt = await handler.WaitForCircleDefinitionChange(
                CircleDefinitionChangeType.Updated, circleId, WaitTimeout);
            ClassicAssert.IsNotNull(evt, "Expected a CircleDefinitionChanged{Updated} notification");
            ClassicAssert.AreEqual(circleId, evt.CircleId);
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task DisableAndEnableCircleDefinition_PushesDisabledThenEnabled()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var circleId = Guid.NewGuid();
        var createResponse = await frodo.Network.CreateCircle(circleId, "ws-toggle-circle", SimpleGrant());
        ClassicAssert.IsTrue(createResponse.IsSuccessStatusCode);

        var handler = new ConnectionCircleNotificationSocketHandler();
        await handler.ConnectAsync(frodo);
        try
        {
            var disableResponse = await frodo.Network.DisableCircleDefinition(new GuidId(circleId));
            ClassicAssert.IsTrue(disableResponse.IsSuccessStatusCode);

            var disabled = await handler.WaitForCircleDefinitionChange(
                CircleDefinitionChangeType.Disabled, circleId, WaitTimeout);
            ClassicAssert.IsNotNull(disabled, "Expected a CircleDefinitionChanged{Disabled} notification");

            var enableResponse = await frodo.Network.EnableCircleDefinition(new GuidId(circleId));
            ClassicAssert.IsTrue(enableResponse.IsSuccessStatusCode);

            var enabled = await handler.WaitForCircleDefinitionChange(
                CircleDefinitionChangeType.Enabled, circleId, WaitTimeout);
            ClassicAssert.IsNotNull(enabled, "Expected a CircleDefinitionChanged{Enabled} notification");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task DeleteCircleDefinition_PushesDeletedToOwnerSessions()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var circleId = Guid.NewGuid();
        var createResponse = await frodo.Network.CreateCircle(circleId, "ws-delete-circle", SimpleGrant());
        ClassicAssert.IsTrue(createResponse.IsSuccessStatusCode);

        var handler = new ConnectionCircleNotificationSocketHandler();
        await handler.ConnectAsync(frodo);
        try
        {
            var deleteResponse = await frodo.Network.DeleteCircleDefinition(new GuidId(circleId));
            ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);

            var evt = await handler.WaitForCircleDefinitionChange(
                CircleDefinitionChangeType.Deleted, circleId, WaitTimeout);
            ClassicAssert.IsNotNull(evt, "Expected a CircleDefinitionChanged{Deleted} notification");
            ClassicAssert.AreEqual(circleId, evt.CircleId);
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }
}
