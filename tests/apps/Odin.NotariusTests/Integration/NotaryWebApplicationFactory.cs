using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.SQLite.NotaryDatabase;
using Odin.KeyChain;

namespace Odin.NotariusTests.Integration;

internal class NotaryWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing KeyChainDatabase service
            var dbDescriptor = services.SingleOrDefault(x => x.ServiceType == typeof(NotaryDatabase));
            if (dbDescriptor != null)
            {
                services.Remove(dbDescriptor);
            }

            // Create new KeyChainDatabase in memory
            //var db = new NotaryDatabase("DataSource=:memory:");
            var db = new NotaryDatabase("DataSource=ondiskfornow.db");
            using (var conn = db.CreateDisposableConnection())
            {
                NotaryDatabaseUtil.InitializeDatabaseAsync(db, conn).Wait();
            }
            services.AddSingleton(db);
        });
        builder.UseEnvironment("Development");
    }
}

