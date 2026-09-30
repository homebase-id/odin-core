using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.JobManagement;
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

        [Description("Purge a moved copy even though it has email: its mailbox here is the only copy of its mail")]
        [CommandOption("--discard-mail")]
        public bool DiscardMail { get; set; }

    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
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
                var response = await httpClient.DeleteAsync(
                    $"tenants/{settings.TenantDomain}" + (settings.DiscardMail ? "?discard-mail=true" : ""));
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new Exception($"Tenant {settings.TenantDomain} was not found");
                }
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    throw new Exception($"Refused: {await response.Content.ReadAsStringAsync()}");
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

                var done = false;
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200));

                    response = await httpClient.GetAsync(location);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
                    }
                    var (jobResponse, jobData) = JobApiResponse.Deserialize<DeleteTenantJobData>(await response.Content.ReadAsStringAsync());

                    if (jobResponse.State == JobState.Failed)
                    {
                        throw new Exception($"Error deleting tenant {settings.TenantDomain}: {jobResponse.Error}");
                    }

                    if (jobResponse.State == JobState.Succeeded)
                    {
                        if (jobData?.KeepDns == true)
                        {
                            AnsiConsole.MarkupLine("DNS kept: it belongs to the host the identity moved to");
                        }
                        AnsiConsole.MarkupLine("[green]Done[/]");
                        done = true;
                    }

                } while (!done);
            });

        return 0;
    }

}