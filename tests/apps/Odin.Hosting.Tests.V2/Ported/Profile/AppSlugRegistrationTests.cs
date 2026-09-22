using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// An app names its own slug at registration. The slug is a package name rather than a role -- a second
/// chat implementation picks <c>chatty</c> instead of occupying <c>chat</c> -- and registration is
/// first-come.
/// </summary>
/// <remarks>
/// Required, and that is the point of these tests: an app that omits the field is refused rather than
/// handed an address derived from its display name, and the slugs of the apps that ship with an identity
/// are not available to anyone else.
/// </remarks>
[TestFixture]
public class AppSlugRegistrationTests : V2Fixture
{
    [Test]
    public async Task ARequestedSlugIsTakenVerbatim()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var appId = Guid.NewGuid();
        await apps.RegisterAppAsync(new AppRegistrationRequest
        {
            AppId = appId,
            Name = "Acme Receipts",
            AppSlug = "receipts",
            PermissionSet = new PermissionSet()
        }, ctx);

        var reg = await apps.GetAppRegistration(appId, ctx);

        // Not "acme-receipts": the app asked for an address and got the one it asked for.
        Assert.That(reg!.AppSlug, Is.EqualTo("receipts"));
    }

    [Test]
    public async Task AnOmittedSlugIsRefused()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var appId = Guid.NewGuid();

        // Deriving one from the display name is what produced addresses nobody chose -- "Homebase -
        // Location" became "homebase-locat", permanently, because a slug cannot be changed afterwards.
        var ex = Assert.ThrowsAsync<OdinClientException>(async () =>
            await apps.RegisterAppAsync(new AppRegistrationRequest
            {
                AppId = appId,
                Name = "Acme Receipts",
                PermissionSet = new PermissionSet()
            }, ctx));

        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.ArgumentError));
        Assert.That(await apps.GetAppRegistration(appId, ctx), Is.Null, "Nothing should have been written");
    }

    [Test]
    public async Task ABuiltInSlugIsReservedForItsOwnApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        // "chat" is the address every client already builds for the chat app. First-come cannot apply
        // to it: whoever asked first would put another app where those clients are looking.
        var ex = Assert.ThrowsAsync<OdinClientException>(async () =>
            await apps.RegisterAppAsync(new AppRegistrationRequest
            {
                AppId = Guid.NewGuid(),
                Name = "Not Chat",
                AppSlug = "chat",
                PermissionSet = new PermissionSet()
            }, ctx));

        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.IdAlreadyExists));
    }

    [Test]
    public async Task AMalformedSlugIsRejectedRatherThanCoerced()
    {
        var owner = await LoginAsOwner(Identities.Merry);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        // Lowercasing this would hand the caller an address it did not ask for.
        var ex = Assert.ThrowsAsync<OdinClientException>(async () =>
            await apps.RegisterAppAsync(new AppRegistrationRequest
            {
                AppId = Guid.NewGuid(),
                Name = "Shouty",
                AppSlug = "Receipts",
                PermissionSet = new PermissionSet()
            }, ctx));

        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.ArgumentError));
    }

    [Test]
    public async Task ATakenSlugIsRefused_NotSilentlyReplaced()
    {
        var owner = await LoginAsOwner(Identities.Pippin);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        await apps.RegisterAppAsync(new AppRegistrationRequest
        {
            AppId = Guid.NewGuid(),
            Name = "First",
            AppSlug = "ledger",
            PermissionSet = new PermissionSet()
        }, ctx);

        // Registration is first-come. Handing the second app a different address would be worse than
        // refusing: it asked for one specific name.
        var ex = Assert.ThrowsAsync<OdinClientException>(async () =>
            await apps.RegisterAppAsync(new AppRegistrationRequest
            {
                AppId = Guid.NewGuid(),
                Name = "Second",
                AppSlug = "ledger",
                PermissionSet = new PermissionSet()
            }, ctx));

        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.IdAlreadyExists));
    }

}
