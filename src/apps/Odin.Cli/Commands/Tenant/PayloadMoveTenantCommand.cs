using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Extensions;
using Odin.Cli.Factories;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Registry.PayloadMove;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Show a tenant's payload move: the handoff on the source, the transfer on the target")]
public sealed class PayloadMoveTenantCommand : AsyncCommand<PayloadMoveTenantCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";

        [Description("Run the transfer again from the newest file (on the target; what already arrived is skipped)")]
        [CommandOption("--retry")]
        public bool Retry { get; set; }
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var path = $"tenants/{settings.TenantDomain}/payload-move";

        if (settings.Retry)
        {
            var retry = await httpClient.PostAsync($"{path}/retry", null);
            if (retry.StatusCode == HttpStatusCode.NotFound)
            {
                throw new Exception($"{settings.TenantDomain} has no payload transfer on this host");
            }
            if (retry.StatusCode == HttpStatusCode.Conflict)
            {
                throw new Exception("The transfer is running a slice right now; try again in a few minutes");
            }
            if (retry.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"{retry.RequestMessage?.RequestUri}: " + retry.StatusCode);
            }
            AnsiConsole.MarkupLine("[green]Transfer re-armed from the newest file[/]");
        }

        var response = await httpClient.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Tenant {settings.TenantDomain} was not found");
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
        }

        var report = OdinSystemSerializer.Deserialize<PayloadMoveReport>(await response.Content.ReadAsStringAsync())
                     ?? new PayloadMoveReport();

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        void Row(string label, Text value) => grid.AddRow(new Text(label, new Style(Color.Blue)), value);

        Row("Domain", new Text(report.Domain));

        if (report.Source == null && report.Target == null)
        {
            Row("Payload move", new Text("none on this host"));
        }

        if (report.Source is { } source)
        {
            Row("Source", new Text(source.CompletedAt != null
                ? $"complete {Show(source.CompletedAt)}"
                : source.RedeemedAt != null
                    ? $"transferring since {Show(source.RedeemedAt)}"
                    : $"exported; handoff expires {Show(source.HandoffExpiresAt)}"));
            Row("Deletable", new Text(source.Pending ? "no, a target may still need the payloads" : "yes"));
        }

        if (report.Target is { } target)
        {
            var p = target.Progress;
            Row("Transfer", new Text($"{p.Status} (job {target.JobState}, next run {Show(target.NextRun)})"));
            Row("From", new Text(p.BaseUrl));
            Row("Files", new Text($"{p.Files} (newest first, down from row {p.StartRowId}; now below {p.CursorRowId})"));
            Row("Objects", new Text($"{p.Objects} moved, {p.Bytes.HumanReadableBytes()}; {p.Skipped} already here"));
            Row("Failures", new Text(p.FailureCount.ToString()));
            if (p.BackoffSeconds > 0)
            {
                Row("Waiting", new Text($"{p.BackoffSeconds} s on the source"));
            }
            if (!string.IsNullOrEmpty(target.LastError))
            {
                Row("Last error", new Text(target.LastError));
            }
        }

        AnsiConsole.Write(grid);

        if (report.Target?.Progress.Failures is { Count: > 0 } failures)
        {
            AnsiConsole.WriteLine();
            foreach (var failure in failures)
            {
                AnsiConsole.WriteLine(failure);
            }
        }

        return 0;
    }

    private static string Show(UnixTimeUtc? time) =>
        time == null ? "-" : time.Value.ToDateTimeOffset().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
