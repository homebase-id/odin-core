using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

public class CircleNetworkListTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    [Test]
    public async Task GetCirclesWithMembers_ListsCreatedCircle_WithRedactedPermissions()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var circleId = Guid.NewGuid();
        var grant = new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>(),
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnections })
        };

        var created = await owner.Admin.CreateCircle(circleId, "Test Circle", grant);
        Assert.That(created.IsSuccessStatusCode, Is.True, $"CreateCircle failed: {created.StatusCode}");

        var client = new V2ConnectionNetworkClient(owner.Identity, owner.Factory);
        var response = await client.GetCirclesWithMembersAsync(includeSystemCircle: false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");

        var mine = response.Content!.SingleOrDefault(c => c.Circle.Id == circleId);
        Assert.That(mine, Is.Not.Null, "the created circle should be listed");
        Assert.That(mine!.Circle.Name, Is.EqualTo("Test Circle"));
        Assert.That(mine.Members, Is.Empty, "no members have been granted yet");

        // The permission set is redacted to keys only.
        Assert.That(mine.Circle.Permissions, Is.Not.Null);
        Assert.That(mine.Circle.Permissions.Keys, Does.Contain(PermissionKeys.ReadConnections));
    }
}
