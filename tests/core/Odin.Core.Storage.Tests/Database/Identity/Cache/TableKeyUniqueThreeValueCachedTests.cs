using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Cache;

public class TableKeyUniqueThreeValueCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldBeImplemented()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        Assert.Fail("This test is not implemented yet.");
    }

    //

}


