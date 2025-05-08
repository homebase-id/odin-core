using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Extensions;
using Odin.Cli.Factories;
using Odin.Core.Serialization;
using Odin.Services.Admin.Tenants;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenants;

[Description("List all tenants in root directory")]
public sealed class ListTenantsCommand : AsyncCommand<ListTenantsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public enum OutputType
        {
            table,
            tree,
            domain,
            id
        }

        [Description("Include payload sizes (slow)")]
        [CommandOption("-p|--payload")]
        public bool IncludePayload { get; set; } = false;

        [Description("Output type:\n'table': show as table (default)\n'tree': show as tree\n'domain': domain only\n'id': id only")]
        [CommandOption("-o|--output <VALUE>")]
        public OutputType Output { get; set; } = OutputType.table;
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var response =
            await httpClient.GetAsync("tenants?include-payload=" + (settings.IncludePayload ? "true" : "false"));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
        }
        var json = await response.Content.ReadAsStringAsync();
        var tenants = OdinSystemSerializer.Deserialize<List<TenantModel>>(json) ?? [];
        tenants.Sort((a,b) => string.Compare(a.Domain, b.Domain, StringComparison.InvariantCultureIgnoreCase));

        return settings.Output switch
        {
            Settings.OutputType.tree => Tree(context, settings, tenants),
            Settings.OutputType.domain => Domain(context, settings, tenants),
            Settings.OutputType.id => Id(context, settings, tenants),
            _ => Table(context, settings, tenants)
        };
    }

    //

    private static int Table(CommandContext context, Settings settings, List<TenantModel> tenants)
    {
        var grid = new Grid();
        grid.AddColumn(); // Domain
        grid.AddColumn(); // Id
        grid.AddColumn(); // Enabled
        grid.AddColumn(); // Registration Size
        grid.AddColumn(); // Payload Size

        if (!settings.NoHeaders)
        {
            grid.AddRow(
                new Text("Domain", new Style(Color.Blue)).LeftJustified(),
                new Text("Id", new Style(Color.Blue)).LeftJustified(),
                new Text("Enabled", new Style(Color.Blue)).LeftJustified(),
                new Text("Reg. Size", new Style(Color.Blue)).RightJustified(),
                new Text("Payload Size", new Style(Color.Blue)).RightJustified());
        }

        foreach (var tenant in tenants)
        {
            var payLoadSize = settings.IncludePayload ? tenant.PayloadSize?.HumanReadableBytes() ?? "n/a" : "n/a";
            grid.AddRow(
                new Text(tenant.Domain).LeftJustified(),
                new Text(tenant.Id).LeftJustified(),
                new Text(tenant.Enabled ? "yes" : "no").RightJustified(),
                new Text(tenant.RegistrationSize.HumanReadableBytes()).RightJustified(),
                new Text(payLoadSize).RightJustified());
        }
        AnsiConsole.Write(grid);

        return 0;
    }

    //

    private static int Tree(CommandContext context, Settings settings, List<TenantModel> tenants)
    {
        var root = new Tree("[bold blue]Tenants[/]");
        foreach (var tenant in tenants)
        {
            var enabled = tenant.Enabled ? "yes" : "no";
            var t = root.AddNode($"[blue]{tenant.Domain}[/]");
            t.AddNode($"[blue]Id:[/] {tenant.Id}");
            t.AddNode($"[blue]Enabled:[/] {enabled}");
            t.AddNode($"[blue]Registration Size:[/] {tenant.RegistrationSize.HumanReadableBytes()}");

            if (settings.IncludePayload)
            {
                var size = tenant.PayloadSize?.HumanReadableBytes() ?? "n/a";
                t.AddNode($"[blue]Payload Size:[/] {size}");
            }
        }

        AnsiConsole.Write(root);

        return 0;
    }

    //

    private static int Domain(CommandContext context, Settings settings, List<TenantModel> tenants)
    {
        var grid = new Grid();
        grid.AddColumn(); // Domain

        foreach (var tenant in tenants)
        {
            grid.AddRow(new Text(tenant.Domain));
        }
        AnsiConsole.Write(grid);
        return 0;
    }

    //

    private static int Id(CommandContext context, Settings settings, List<TenantModel> tenants)
    {
        var grid = new Grid();
        grid.AddColumn(); // Id

        foreach (var tenant in tenants)
        {
            grid.AddRow(new Text(tenant.Id));
        }
        AnsiConsole.Write(grid);
        return 0;
    }

    //


}


