using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Background.BackgroundServices.System;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Certificates;

// The sweep leaves a copy that moved away alone: its domain now points at the host it moved to, which renews it
public class UpdateCertificatesSweepTests
{
    [Test]
    public async Task TheSweepRenewsEveryIdentityExceptOneThatMovedAway()
    {
        var registry = Substitute.For<IIdentityRegistry>();
        registry.GetList(Arg.Any<PageOptions>()).Returns(new PagedResult<IdentityRegistration>(PageOptions.All, 1,
            new List<IdentityRegistration>
            {
                Registration("active.example.com", TenantStatus.Active, null),
                Registration("paused.example.com", TenantStatus.Paused, null),
                Registration("admin.example.com", TenantStatus.Disabled, DisabledReason.Admin),
                Registration("moved.example.com", TenantStatus.Disabled, DisabledReason.Moved),
            }));
        var systemDomains = Substitute.For<ISystemDomains>();
        systemDomains.Get().Returns([]);
        var certificates = Substitute.For<ICertificateService>();

        var sweep = new UpdateCertificatesBackgroundService(NullLogger<UpdateCertificatesBackgroundService>.Instance,
            new OdinConfiguration(), certificates, registry, systemDomains);
        await sweep.RenewAllAsync(CancellationToken.None);

        foreach (var renewed in new[] { "active.example.com", "paused.example.com", "admin.example.com" })
        {
            await certificates.Received(1).RenewIfAboutToExpireAsync(renewed, Arg.Any<string[]>(), Arg.Any<CancellationToken>());
        }
        await certificates.DidNotReceive().RenewIfAboutToExpireAsync("moved.example.com", Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    private static IdentityRegistration Registration(string domain, TenantStatus status, DisabledReason? reason) => new()
    {
        Id = Guid.NewGuid(),
        PrimaryDomainName = domain,
        Status = status,
        DisabledReason = reason,
    };
}
