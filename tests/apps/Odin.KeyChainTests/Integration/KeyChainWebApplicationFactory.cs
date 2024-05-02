using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.KeyChain;

namespace Odin.KeyChainTests.Integration;

internal class KeyChainWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing KeyChainDatabase service
            var dbDescriptor = services.SingleOrDefault(x => x.ServiceType == typeof(KeyChainDatabase));
            if (dbDescriptor != null)
            {
                services.Remove(dbDescriptor);
            }

            // Create new KeyChainDatabase in memory
            var db = new KeyChainDatabase("DataSource=:memory:");
            KeyChainDatabaseUtil.InitializeDatabase(db);
            services.AddSingleton(db);
        });
        builder.UseEnvironment("Development");
    }
}

