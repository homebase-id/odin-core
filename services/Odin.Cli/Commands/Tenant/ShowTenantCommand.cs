using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Extensions;
using Odin.Cli.Factories;
using Odin.Cli.Services;
using Odin.Core.Serialization;
using Odin.Core.Services.Admin.Tenants;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Show tenant")]
public sealed class ShowTenantCommand : AsyncCommand<ShowTenantCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";

        [Description("Include payload sizes (slow)")]
        [CommandOption("-p|--payload")]
        public bool IncludePayload { get; set; } = false;
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var response = await httpClient.GetAsync(
            $"tenants/{settings.TenantDomain}?include-payload=" + (settings.IncludePayload ? "true" : "false"));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Tenant {settings.TenantDomain} was not found");
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
        }
        var json = await response.Content.ReadAsStringAsync();
        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(json) ?? new TenantModel();

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
                new Text("Enabled", new Style(Color.Blue)).RightJustified(),
                new Text("Reg. Size", new Style(Color.Blue)).RightJustified(),
                new Text("Payload Size", new Style(Color.Blue)).RightJustified());
        }

        var payLoadSize = tenant.PayloadSize?.HumanReadableBytes() ?? "n/a";
        grid.AddRow(
            new Text(tenant.Domain).LeftJustified(),
            new Text(tenant.Id).LeftJustified(),
            new Text(tenant.Enabled ? "yes" : "no").LeftJustified(),
            new Text(tenant.RegistrationSize.HumanReadableBytes()).RightJustified(),
            new Text(payLoadSize).RightJustified());

        AnsiConsole.Write(grid);

        return 0;
    }

}