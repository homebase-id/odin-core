using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Factory;

public class DatabaseTypeTest : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldReturnCorrectDatabaseType(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, false);

        await using var scope = Services.BeginLifetimeScope();

        var scopedSystemConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();
        Assert.That(scopedSystemConnectionFactory.DatabaseType, Is.EqualTo(databaseType));

        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        Assert.That(scopedIdentityConnectionFactory.DatabaseType, Is.EqualTo(databaseType));
    }
}