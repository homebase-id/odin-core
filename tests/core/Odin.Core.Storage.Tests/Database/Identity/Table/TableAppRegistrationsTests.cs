using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableAppRegistrationsTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task UpdateIfUnmodifiedWritesOnlyOverTheRowAsRead(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var table = scope.Resolve<TableAppRegistrations>();

        var appId = Guid.NewGuid();
        await table.InsertAsync(new AppRegistrationsRecord
        {
            AppId = appId, AppSlug = "cas-app", Name = "Cas App", grantJson = "{\"v\":0}"
        });
        var asRead = await table.GetAsync(appId);

        // Another write lands after the read
        await table.UpdateAsync(asRead with { grantJson = "{\"v\":1}" });

        Assert.That(await table.UpdateIfUnmodifiedAsync(asRead with { grantJson = "{\"v\":2}" }, asRead.modified), Is.False);
        Assert.That((await table.GetAsync(appId)).grantJson, Is.EqualTo("{\"v\":1}"));

        var current = await table.GetAsync(appId);
        Assert.That(await table.UpdateIfUnmodifiedAsync(current with { grantJson = "{\"v\":3}" }, current.modified), Is.True);
        var after = await table.GetAsync(appId);
        Assert.That(after.grantJson, Is.EqualTo("{\"v\":3}"));
        Assert.That(after.modified.milliseconds, Is.GreaterThan(current.modified.milliseconds));
    }
}
