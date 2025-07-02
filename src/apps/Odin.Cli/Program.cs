using System.Diagnostics;
using Odin.Cli.Commands;
using Odin.Cli.Commands.Tenant;
using Odin.Cli.Commands.Tenants;
using Spectre.Console;
using Spectre.Console.Cli;

// https://spectreconsole.net/cli/

// DI currently doesnt work with a custom help provider:
// https://github.com/spectreconsole/spectre.console/issues/1313
//

var app = new CommandApp();

// SEB:NOTE this command is an annoying hack to allow passing in -I and -K when they are not needed.
// Without it, running "./odin-admin -I foo -K bar" gives an error.
app.SetDefaultCommand<DefaultCommand>();

app.Configure(config =>
{
    #if DEBUG
    config.ValidateExamples();
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
        c.AddCommand<ExportTenantCommand>("export")
            .WithExample("tenant", "export", "frodo.dotyou.cloud", "-I", "admin.dotyou.cloud:4444", "-K",
                "your-secret-api-key-here");
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

