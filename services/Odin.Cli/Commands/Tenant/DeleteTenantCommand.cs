using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Extensions;
using Odin.Cli.Factories;
using Odin.Cli.Services;
using Odin.Core.Serialization;
using Odin.Core.Services.Admin.Tenants;
using Odin.Core.Services.Quartz;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Delete tenant")]
public sealed class DeleteTenantCommand : AsyncCommand<DeleteTenantCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";

        [Description("Ignore prompts")]
        [CommandOption("-y|--yes")]
        public bool IgnorePrompts { get; set; } = false;

    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        throw new Exception("SEB:TODO Waiting for multi-payload rewrite. This has problems with database disposal.");

        if (!settings.IgnorePrompts)
        {
            if (!AnsiConsole.Confirm($"Really delete tenant {settings.TenantDomain}?", defaultValue: false))
            {
                return 1;
            }
        }
        
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var response = await httpClient.DeleteAsync($"tenants/{settings.TenantDomain}");
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new Exception($"Tenant {settings.TenantDomain} was not found");
                }
                if (response.StatusCode != HttpStatusCode.Accepted)
                {
                    throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
                }

                response.Headers.TryGetValues("Location", out var locations);
                var location = locations?.FirstOrDefault() ?? "";
                if (location == "")
                {
                    throw new Exception("HTTP response is missing Location header");
                }

                JobState? state;
                do
                {
                    response = await httpClient.GetAsync(location);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
                    }
                    state = OdinSystemSerializer.Deserialize<JobState>(await response.Content.ReadAsStringAsync());
                    if (state == null)
                    {
                        throw new Exception("Error deserializing reponse");
                    }

                    if (state.Status == JobStatusEnum.Failed)
                    {
                        throw new Exception($"Error deleting tenant {settings.TenantDomain}: {state.Error}");
                    }
                } while (state.Status == JobStatusEnum.Unknown);

            });

        return 0;
    }

}