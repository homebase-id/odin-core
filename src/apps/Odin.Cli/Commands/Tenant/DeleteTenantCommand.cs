using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.JobManagement;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Delete a disabled tenant from this host. Never its DNS (delete-identity-dns)")]
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
                await TenantStatusApi.EnsureSuccessAsync(response, settings.TenantDomain, HttpStatusCode.Accepted);

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
                    await ApiResponse.EnsureAsync(response);
                    var jobResponse = JobApiResponse.Deserialize(await response.Content.ReadAsStringAsync());

                    if (jobResponse.State == JobState.Failed)
                    {
                        throw new Exception($"Error deleting tenant {settings.TenantDomain}: {jobResponse.Error}");
                    }

                    if (jobResponse.State == JobState.Succeeded)
                    {
                        AnsiConsole.MarkupLine("[green]Done[/]");
                        AnsiConsole.MarkupLine(
                            $"Its DNS is untouched: `delete-identity-dns {settings.TenantDomain}` removes it, on the host it points at.");
                        done = true;
                    }

                } while (!done);
            });

        return 0;
    }

}