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

// DI currently doesnt work with a custom help provider:
// https://github.com/spectreconsole/spectre.console/issues/1313
//
// var serviceCollection = new ServiceCollection();
// serviceCollection.AddSingleton<ITenantFileSystem, TenantFileSystem>();
// var registrar = new TypeRegistrar(serviceCollection);
// serviceCollection.AddSingleton<IHttpClientFactory, HttpClientFactory>();
// serviceCollection.AddSingleton<ICliHttpClientFactory, CliHttpClientFactory>();

var app = new CommandApp();
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
            .WithExample("tenants", "list", "-I", "admin.dotyou.cloud:4444", "-K", "your-secret-api-key-here");
    });
    config.AddBranch("tenant", c =>
    {
        c.AddCommand<ShowTenantCommand>("show")
            .WithExample("tenant", "show", "frodo.dotyou.cloud", "-I", "admin.dotyou.cloud:4444", "-K",
                "your-secret-api-key-here", "--payload");
        c.AddCommand<DeleteTenantCommand>("delete")
            .WithExample("tenant", "delete", "frodo.dotyou.cloud", "-I", "admin.dotyou.cloud:4444", "-K",
                "your-secret-api-key-here");
        c.AddCommand<DisableTenantCommand>("disable")
            .WithExample("tenant", "disable", "frodo.dotyou.cloud", "-I", "admin.dotyou.cloud:4444", "-K",
                "your-secret-api-key-here");
        c.AddCommand<EnableTenantCommand>("enable")
            .WithExample("tenant", "enable", "frodo.dotyou.cloud", "-I", "admin.dotyou.cloud:4444", "-K",
                "your-secret-api-key-here");
    });
    // config.AddBranch("tenantfs", c =>
    // {
    //     c.AddCommand<ShowTenantFsCommand>("show")
    //         .WithExample("tenantfs", "show", "130c23d5-e76a-421b-927d-92a22a220b54")
    //         .WithExample("tenantfs", "show", "frodo.dotyou.cloud", "--payload")
    //         .WithExample("tenantfs", "show", "/identity-host/data/tenants/130c23d5-e76a-421b-927d-92a22a220b54")
    //         .WithExample("tenantfs", "show", "/identity-host/data/tenants/frodo.dotyou.cloud", "--payload");
    // });
    // config.AddBranch("tenantsfs", c =>
    // {
    //     c.AddCommand<ListTenantsFsCommand>("list")
    //         .WithExample("tenantsfs", "list", "--payload", "--output", "tree")
    //         .WithExample("tenantsfs", "list", "/identity-host/data/tenants", "--quiet");
    // });
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

