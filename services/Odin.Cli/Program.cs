using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
    config.AddBranch("tenants", tenantsConfig =>
    {
        tenantsConfig.AddCommand<ListTenantsCommand>("list");
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

