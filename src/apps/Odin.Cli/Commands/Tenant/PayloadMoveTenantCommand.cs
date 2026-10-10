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

        [Description("Give up the objects the source does not have, so the move completes and the source copy can be " +
                     "deleted (on the target). Only when every failure is \"the source does not have it\"; the " +
                     "transfer asks the source for each once more first. Gives data up: confirm on the source first.")]
        [CommandOption("--accept-missing")]
        public bool AcceptMissing { get; set; }

        public override ValidationResult Validate() => Retry && AcceptMissing
            ? ValidationResult.Error("--retry and --accept-missing are separate steps; run one at a time")
            : base.Validate();
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var path = $"tenants/{settings.TenantDomain}/payload-move";

        async Task PostAsync(string action)
        {
            var posted = await httpClient.PostAsync($"{path}/{action}", null);
            if (posted.StatusCode == HttpStatusCode.NotFound)
            {
                throw new Exception($"{settings.TenantDomain} has no payload transfer on this host");
            }
            if (posted.StatusCode == HttpStatusCode.Conflict)
            {
                throw new Exception("The transfer is running a slice right now; try again in a few minutes");
            }
            await ApiResponse.EnsureAsync(posted);
        }

        if (settings.Retry)
        {
            await PostAsync("retry");
            AnsiConsole.MarkupLine("[green]Transfer re-armed from the newest file[/]");
        }

        if (settings.AcceptMissing)
        {
            await PostAsync("accept-missing");
        }

        var response = await httpClient.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Tenant {settings.TenantDomain} was not found");
        }
        await ApiResponse.EnsureAsync(response);

        var report = OdinSystemSerializer.Deserialize<PayloadMoveReport>(await response.Content.ReadAsStringAsync())
                     ?? new PayloadMoveReport();

        if (settings.AcceptMissing && report.Target is { } accepted)
        {
            var objects = accepted.Progress.Missing;
            AnsiConsole.MarkupLine($"[yellow]Giving up {objects.Count} object(s) the source does not have:[/]");
            foreach (var o in objects)
            {
                AnsiConsole.WriteLine($"  {o}");
            }
            AnsiConsole.MarkupLine("The target asks the source for each once more, then completes the move without them. " +
                                   "Follow it below; it reads Complete once done.");
            AnsiConsole.WriteLine();
        }

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
                ? $"complete {source.CompletedAt.ToCliTime()}"
                : source.RedeemedAt != null
                    ? $"transferring since {source.RedeemedAt.ToCliTime()}"
                    : $"exported; handoff expires {source.HandoffExpiresAt.ToCliTime()}"));
            Row("Deletable", new Text(source.Pending ? "no, a target may still need the payloads" : "yes"));
        }

        if (report.Target is { } target)
        {
            var p = target.Progress;
            Row("Transfer", new Text($"{p.Status} (job {target.JobState}, next run {target.NextRun.ToCliTime()})"));
            Row("From", new Text(p.BaseUrl));
            Row("Queued items", new Text(p.QueuedItemsDone
                ? "payloads fetched"
                : p.HoldsResume ? "payloads arriving; resume waits for them" : "payloads not fetched"));
            Row("Files", new Text($"{p.Files} (newest first, down from row {p.StartRowId}; now below {p.CursorRowId})"));
            Row("Objects", new Text($"{p.Objects} moved, {p.Bytes.HumanReadableBytes()}; {p.Skipped} already here"));
            Row("Failures", new Text(p.FailureCount.ToString()));
            if (p.AcceptedMissingAt != null)
            {
                Row("Gave up", new Text($"{p.AcceptedMissing} object(s) the source did not have, {p.AcceptedMissingAt.ToCliTime()}"));
            }
            else if (p.Status == PayloadMoveStatus.AcceptingMissing)
            {
                Row("Accepting", new Text($"{p.MissingCount} missing object(s): checking them at the source once more"));
            }
            else if (p.Status == PayloadMoveStatus.CompleteWithFailures)
            {
                Row("Missing", new Text(p.WhyMissingCannotBeAccepted is { } why
                    ? $"{p.MissingCount} at the source; cannot be accepted: {why}"
                    : $"{p.MissingCount} at the source, and nothing else failed: --accept-missing gives them up"));
            }
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
}
