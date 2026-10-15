using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.InvitationCode;

[Description("List the invitation codes an identity owns, with uses left")]
public sealed class ListInvitationCodesCommand : AsyncCommand<ListInvitationCodesCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Identity that owns the codes")]
        [CommandArgument(0, "<owner>")]
        public string Owner { get; init; } = "";
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var response = await httpClient.GetAsync($"invitation-codes?owner={Uri.EscapeDataString(settings.Owner)}");
        await ApiResponse.EnsureAsync(response);

        var codes = await response.Content.ReadFromJsonAsync<List<InvitationCodeResponse>>() ?? [];
        codes.ForEach(InvitationCodeResponse.Write);
        return 0;
    }
}
