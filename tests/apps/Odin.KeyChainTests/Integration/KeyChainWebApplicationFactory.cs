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
            // var db = new KeyChainDatabase(":memory:");

            // I have an issue with the memory database getting cleared
            // I couldn't figure out why, since the connection should stay open.
            // So made it a file for now. Which might fail I suppose if some of the tests
            // relies on an exclusive database.
            //
            var db = new KeyChainDatabase("helpsasafile.db"); 
            var memoryConnection = db.CreateDisposableConnection();
            KeyChainDatabaseUtil.InitializeDatabaseAsync(db, memoryConnection).Wait();
            services.AddSingleton(db);
            services.AddSingleton(memoryConnection);
        });
        builder.UseEnvironment("Development");
    }
}

