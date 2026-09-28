#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// Deleting a registration disables it first, whoever calls the delete: requests get a 503 and background services
/// stop, so nothing writes to the identity while its rows and storage are removed (#1792).
/// </summary>
[TestFixture]
public class DeleteRegistrationDisablesFirstTests : V2Fixture
{
    [Test]
    public async Task DeletingAnActiveRegistrationDisablesItFirst()
    {
        var registry = Host.Server.Services.GetRequiredService<IIdentityRegistry>();

        // A dev domain with a dev certificate that no test identity uses: registered with that certificate,
        // so none is requested for it.
        const string domain = "dev.dotyou.cloud";
        var certificates = Path.Combine(Host.Server.Services.GetRequiredService<OdinConfiguration>().Development.SslSourcePath, domain);
        await registry.AddRegistration(new IdentityRegistrationRequest
        {
            OdinId = new OdinId(domain),
            Email = $"delete@{domain}",
            PlanId = "",
            OptionalCertificatePemContent = new CertificatePemContent
            {
                Certificate = await File.ReadAllTextAsync(Path.Combine(certificates, "certificate.crt")),
                PrivateKey = await File.ReadAllTextAsync(Path.Combine(certificates, "private.key"))
            }
        });
        Assert.That((await registry.GetAsync(domain)).Status, Is.EqualTo(TenantStatus.Active));

        Host.LogStore.Clear();

        // Straight to the registry, the way IdentityRegistrationService deletes, without DeleteTenantJob's help.
        await registry.DeleteRegistration(domain);

        var messages = Host.LogStore.GetLogEvents().SelectMany(kv => kv.Value).Select(e => e.RenderMessage()).ToList();
        Assert.That(messages, Has.Some.Contains($"Status of \"{domain}\" set to Disabled (reason: PendingDeletion)"),
            "the registration must be disabled before it is deleted");
        Assert.That(await registry.GetAsync(domain), Is.Null);
    }
}
