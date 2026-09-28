#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Services.Tenant.Container;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Registry;
using Serilog.Events;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// A registration that fails to load must show in the load's summary line, not only in its own error (#1701). On
/// 2026-09-01 every tenant on a host failed to load, and the only aggregate line was an Information one.
/// </summary>
[TestFixture]
public class RegistrationLoadSummaryTests : V2Fixture
{
    // The behaviour under test: the injected registration fails to load, and says so.
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings => ["Error loading registration"];

    [Test]
    public async Task AFailedRegistrationIsReportedInTheSummaryAtWarning()
    {
        var badId = Guid.NewGuid();
        await InsertUnloadableRegistrationAsync(badId);
        try
        {
            Host.LogStore.Clear();

            // The host's own identities are already loaded and are skipped; only the new row is attempted.
            await Host.Server.Services.GetRequiredService<IIdentityRegistry>().LoadRegistrations();

            var summaries = Host.LogStore.GetLogEvents()
                .Where(kv => kv.Key >= LogEventLevel.Warning)
                .SelectMany(kv => kv.Value)
                .Select(e => e.RenderMessage())
                .Where(m => m.Contains("Registry loaded at version"))
                .ToList();

            Assert.That(summaries, Has.Some.Contains("1 failed"),
                "the summary must report the failed registration at Warning or above");
        }
        finally
        {
            await WithSystemDatabaseAsync(db => db.Registrations.DeleteAsync(badId));
        }
    }

    /// <summary>
    /// The 2026-09-01 case is the Error row: registrations exist and none is serving.
    /// </summary>
    [TestCase(0, 0, 100, LogLevel.Error)]
    [TestCase(3, 0, 1, LogLevel.Warning)]
    [TestCase(0, 3, 1, LogLevel.Warning)]
    [TestCase(3, 2, 0, LogLevel.Information)]
    [TestCase(0, 0, 0, LogLevel.Information)]
    public void TheSummaryLevelReflectsHowManyRegistrationsAreServing(int loaded, int alreadyLoaded, int failed,
        LogLevel expected)
    {
        Assert.That(FileSystemIdentityRegistry.LoadSummaryLevel(loaded, alreadyLoaded, failed), Is.EqualTo(expected));
    }

    /// <summary>A registration row whose first-run token is not a GUID: loading it throws, per registration.</summary>
    private Task InsertUnloadableRegistrationAsync(Guid id) =>
        WithSystemDatabaseAsync(db => db.Registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = id,
            email = "unloadable@example.com",
            primaryDomainName = $"unloadable-{id:N}.dotyou.cloud",
            firstRunToken = "not-a-guid",
            planId = "",
            json = "{}"
        }));

    private async Task WithSystemDatabaseAsync(Func<SystemDatabase, Task> action)
    {
        var container = Host.Server.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.BeginLifetimeScope(nameof(RegistrationLoadSummaryTests));
        await action(scope.Resolve<SystemDatabase>());
    }
}
