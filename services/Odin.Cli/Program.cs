
using Microsoft.Extensions.DependencyInjection;
using Odin.Cli.Infrastructure;
using Odin.Cli.Commands.Tenants;
using Spectre.Console.Cli;

// Create a type registrar and register any dependencies.
// A type registrar is an adapter for a DI framework.
var registrations = new ServiceCollection();
var registrar = new TypeRegistrar(registrations);

var app = new CommandApp(registrar);
app.Configure(config =>
{
    config.SetApplicationName("odin-admin");
    config.AddBranch("tenants", tenantsConfig =>
    {
        tenantsConfig.AddCommand<ListTenantsCommand>("list");
    });

});

return await app.RunAsync(args);
