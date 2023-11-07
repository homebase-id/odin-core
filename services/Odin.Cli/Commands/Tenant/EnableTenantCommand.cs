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

[Description("Enable tenant")]
public sealed class EnableTenantCommand : AsyncCommand<EnableTenantCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var response = await httpClient.PatchAsync($"tenants/{settings.TenantDomain}/enable", null);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Tenant {settings.TenantDomain} was not found");
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
        }
        return 0;
    }

}