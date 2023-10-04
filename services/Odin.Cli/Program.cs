using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Odin.Cli.Commands.Tenant;
using Odin.Cli.Infrastructure;
using Odin.Cli.Commands.Tenants;
using Odin.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

// https://spectreconsole.net/cli/

var serviceCollection = new ServiceCollection();
serviceCollection.AddSingleton<ITenantFileSystem, TenantFileSystem>();
var registrar = new TypeRegistrar(serviceCollection);

var app = new CommandApp(registrar);
app.Configure(config =>
{
    config.PropagateExceptions();
    config.SetApplicationName("odin-admin");
    config.AddBranch("tenant", c =>
    {
        c.AddCommand<ShowTenantCommand>("show")
            .WithExample("tenant", "show", "130c23d5-e76a-421b-927d-92a22a220b54")
            .WithExample("tenant", "show", "frodo.dotyou.cloud", "--payload")
            .WithExample("tenant", "show", "/identity-host/data/tenants/130c23d5-e76a-421b-927d-92a22a220b54")
            .WithExample("tenant", "show", "/identity-host/data/tenants/frodo.dotyou.cloud", "--payload");

    });
    config.AddBranch("tenants", c =>
    {
        c.AddCommand<ListTenantsCommand>("list")
            .WithExample("tenants", "list", "--payload", "--tree")
            .WithExample("tenants", "list", "/identity-host/data/tenants", "--quiet");
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

