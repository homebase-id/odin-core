using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Odin.Cli.Commands.Tenant;
using Odin.Cli.Infrastructure;
using Odin.Cli.Commands.Tenants;
using Odin.Cli.Factories;
using Odin.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;
using HttpClientFactory = HttpClientFactoryLite.HttpClientFactory;

// https://spectreconsole.net/cli/

var serviceCollection = new ServiceCollection();
serviceCollection.AddSingleton<ITenantFileSystem, TenantFileSystem>();
var registrar = new TypeRegistrar(serviceCollection);

serviceCollection.AddSingleton<IHttpClientFactory, HttpClientFactory>();
serviceCollection.AddSingleton<ICliHttpClientFactory, CliHttpClientFactory>();

var app = new CommandApp(registrar);
app.Configure(config =>
{
    #if DEBUG
    // config.ValidateExamples();
    #endif

    config.SetApplicationName("odin-admin");
    config.PropagateExceptions();
    config.AddBranch("tenants", c =>
    {
        c.AddCommand<ListTenantsCommand>("list")
            .WithExample("tenants", "list", "--api-key", "your-secret-api-key-here");
    });
    config.AddBranch("tenantfs", c =>
    {
        c.AddCommand<ShowTenantFsCommand>("show")
            .WithExample("tenantfs", "show", "130c23d5-e76a-421b-927d-92a22a220b54")
            .WithExample("tenantfs", "show", "frodo.dotyou.cloud", "--payload")
            .WithExample("tenantfs", "show", "/identity-host/data/tenants/130c23d5-e76a-421b-927d-92a22a220b54")
            .WithExample("tenantfs", "show", "/identity-host/data/tenants/frodo.dotyou.cloud", "--payload");
    });
    config.AddBranch("tenantsfs", c =>
    {
        c.AddCommand<ListTenantsFsCommand>("list")
            .WithExample("tenantsfs", "list", "--payload", "--output", "tree")
            .WithExample("tenantsfs", "list", "/identity-host/data/tenants", "--quiet");
    });
});

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    if (Debugger.IsAttached)
    {
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
    }
    return 1;
}

