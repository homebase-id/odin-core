using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Odin.NotariusTests.Integration;

internal class NotaryWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, configBuilder) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                {"ConnectionStrings:DatabasePath", ":memory:"}
            };
            configBuilder.AddInMemoryCollection(testConfig);
        });
        builder.UseEnvironment("Development");
    }
}

