using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Core.Serialization;
using Odin.Core.Services.Admin.Tenants.Jobs;
using Odin.Core.Services.Quartz;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Export tenant")]
public sealed class ExportTenantCommand : AsyncCommand<ExportTenantCommand.Settings>
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
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var response = await httpClient.PostAsync($"tenants/{settings.TenantDomain}/export", null);
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

                var done = false;
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200));

                    response = await httpClient.GetAsync(location);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
                    }
                    var (jobResponse, jobData) = JobResponse.Deserialize<ExportTenantData>(await response.Content.ReadAsStringAsync());

                    if (jobResponse.Status == JobStatusEnum.Failed)
                    {
                        throw new Exception($"Error exporting tenant {settings.TenantDomain}: {jobResponse.Error}");
                    }

                    if (jobResponse.Status == JobStatusEnum.Completed)
                    {
                        AnsiConsole.MarkupLine($"[green]Done[/]. Copy of tenant on server: {jobData?.TargetPath}");
                        done = true;
                    }

                } while (!done);
            });

        return 0;
    }

}