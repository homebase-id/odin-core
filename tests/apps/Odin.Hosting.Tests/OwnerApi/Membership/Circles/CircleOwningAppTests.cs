using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Circles
{
    /// <summary>
    /// Giving a circle an owning app, and moving it once it has one.
    /// </summary>
    /// <remarks>
    /// Its own fixture rather than more cases on <see cref="CircleDefinitionTests"/>: these create
    /// circles, that class asserts on how many circles the identity has, and NUnit runs both against
    /// the same scaffold and identity.  Sharing would make a count assertion there fail every time a
    /// case is added here, which is exactly what happened.
    /// </remarks>
    public class CircleOwningAppTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Samwise });
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }

        //
        // Adoption: giving an unowned circle an owning app.
        //
        // A circle with no AppId reads as the owner's own, which is right for most and wrong for the
        // ones that predate app ownership.  Adoption is one-way -- it fills an empty owner, never moves
        // a set one -- because PendingEnrollment denormalises AppId on the promise that ownership does
        // not change.
        //

        [Test]
        public async Task AdoptingAnUnownedCircleGivesItTheApp()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle awaiting an owner",
                    Description = "Created without an app, as everything before app ownership was",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                var create = await svc.CreateCircleDefinition(request);
                ClassicAssert.IsTrue(create.IsSuccessStatusCode, $"Failed.  Actual response {create.StatusCode}");

                var before = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(before, Is.Not.Null);
                Assert.That(before.AppId, Is.Null, "a circle created without an app must start unowned");

                var adopt = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = appId
                });
                Assert.That(adopt.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {adopt.StatusCode}");

                var after = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(after, Is.Not.Null);
                Assert.That(after.AppId, Is.EqualTo(appId));

                // Adoption names an administrator.  Everything the circle actually grants is untouched.
                Assert.That(after.Name, Is.EqualTo(before.Name));
                Assert.That(after.Description, Is.EqualTo(before.Description));
                Assert.That(after.Permissions.HasKey(PermissionKeys.ReadCircleMembership), Is.True);
                Assert.That(after.GrantOn, Is.EqualTo(before.GrantOn));
                Assert.That(after.Designation, Is.EqualTo(before.Designation));
                Assert.That(after.Emoji, Is.EqualTo(before.Emoji));
                Assert.That(after.Disabled, Is.EqualTo(before.Disabled));
                Assert.That(after.Created, Is.EqualTo(before.Created));
            }
        }

        [Test]
        public async Task AdoptingACircleThatAlreadyHasAnAppIsRefused()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var firstAppId = Guid.NewGuid();
            var secondAppId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(firstAppId, new PermissionSetGrantRequest());
            await ownerClient.Apps.RegisterApp(secondAppId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle adopted once",
                    Description = "Ownership is set once and never moved",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);
                ClassicAssert.IsTrue((await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = firstAppId
                })).IsSuccessStatusCode);

                var second = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = secondAppId
                });

                Assert.That(second.IsSuccessStatusCode, Is.False, "ownership must not be reassignable");
                Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

                var after = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(after!.AppId, Is.EqualTo(firstAppId), "the refused call must not have moved it");
            }
        }

        [Test]
        public async Task ReAdoptingByTheSameAppIsRefused()
        {
            // Not idempotent on purpose: a repeat is indistinguishable from two apps racing for the
            // circle, and that is the reading worth failing on.
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle adopted twice by one app",
                    Description = "",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);

                var body = new SetCircleOwningAppRequest { CircleId = request.Id, AppId = appId };
                ClassicAssert.IsTrue((await svc.SetCircleOwningApp(body)).IsSuccessStatusCode);

                var again = await svc.SetCircleOwningApp(body);
                Assert.That(again.IsSuccessStatusCode, Is.False);
                Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            }
        }

        [Test]
        public async Task AdoptingByAnUnregisteredAppIsRefused()
        {
            // Checked before the write: stamping an app that does not exist would leave the circle in
            // the very state adoption exists to escape -- owned by something that can never claim it,
            // and no longer adoptable.
            var identity = TestIdentities.Samwise;

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle offered to nobody",
                    Description = "",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);

                var response = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = Guid.NewGuid()
                });

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

                var after = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(after!.AppId, Is.Null, "the circle must still be adoptable");
            }
        }

        [Test]
        public async Task AdoptingACircleThatDoesNotExistIsRefused()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var response = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = Guid.NewGuid(),
                    AppId = appId
                });

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            }
        }

        //
        // Reassignment: the escape hatch out of the one-way rule above.
        //

        [Test]
        public async Task ReassigningMovesTheCircleToTheNewApp()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var firstAppId = Guid.NewGuid();
            var secondAppId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(firstAppId, new PermissionSetGrantRequest());
            await ownerClient.Apps.RegisterApp(secondAppId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle that moves",
                    Description = "",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);
                ClassicAssert.IsTrue((await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = firstAppId
                })).IsSuccessStatusCode);

                var response = await svc.ReassignCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = secondAppId
                });

                Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");
                Assert.That(response.Content, Is.Not.Null);
                Assert.That(response.Content.EnrollmentsRepointed, Is.EqualTo(0),
                    "no enrollments were queued against this circle");

                var after = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(after!.AppId, Is.EqualTo(secondAppId));

                // Reassignment moves ownership only; it must not disturb what the circle grants.
                Assert.That(after.Name, Is.EqualTo(request.Name));
                Assert.That(after.Permissions.HasKey(PermissionKeys.ReadCircleMembership), Is.True);
            }
        }

        [Test]
        public async Task ReassigningAnUnownedCircleIsAllowed()
        {
            // Reassign is the escape hatch, not a stricter adopt: it does not care what the circle
            // came from, only that the destination is real and the circle is not a system one.
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Never owned",
                    Description = "",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);

                var response = await svc.ReassignCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = appId
                });

                Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");
                Assert.That(((await svc.GetCircleDefinition(request.Id)).Content)!.AppId, Is.EqualTo(appId));
            }
        }

        [Test]
        public async Task ReassigningASystemCircleIsRefused()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var response = await svc.ReassignCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = SystemCircleConstants.ConfirmedConnectionsCircleId.Value,
                    AppId = appId
                });

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            }
        }

        [Test]
        public async Task ReassigningToAnUnregisteredAppIsRefused()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Going nowhere",
                    Description = "",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);
                ClassicAssert.IsTrue((await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = appId
                })).IsSuccessStatusCode);

                var response = await svc.ReassignCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = Guid.NewGuid()
                });

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(((await svc.GetCircleDefinition(request.Id)).Content)!.AppId, Is.EqualTo(appId),
                    "the refused call must not have moved it");
            }
        }
    }
}
