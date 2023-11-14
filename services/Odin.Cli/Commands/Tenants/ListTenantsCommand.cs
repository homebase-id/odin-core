using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
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
            grid,
            tree,
        }

        [Description("Include payload sizes (slow)")]
        [CommandOption("-p|--payload")]
        public bool IncludePayload { get; set; } = false;

        [Description("Only show domains")]
        [CommandOption("-q|--quiet")]
        public bool OnlyShowDomains { get; set; } = false;

        [Description("Output type:\n'grid': show as grid (default)\n'tree': show as tree")]
        [CommandOption("-o|--output")]
        public OutputType Output { get; set; } = OutputType.grid;
    }

    //

    private readonly ICliHttpClientFactory _cliHttpClientFactory;

    public ListTenantsCommand(ICliHttpClientFactory cliHttpClientFactory)
    {
        _cliHttpClientFactory = cliHttpClientFactory;
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = _cliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var response = await httpClient.GetAsync("ping");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
        }
        var result = await response.Content.ReadAsStringAsync();
        AnsiConsole.MarkupLine($"[green]pingn:[/] {result}");
        return 0;
    }

    //


}


