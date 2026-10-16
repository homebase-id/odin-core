using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using Odin.Cli.Commands.Base;
using Odin.Services.Registry.Registration;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.InvitationCode;

[Description("List the invitation codes an identity owns, with uses left")]
public sealed class ListInvitationCodesCommand : AsyncCommand<ListInvitationCodesCommand.Settings>
{
    public sealed class Settings : InvitationCodeSettings
    {
        [Description("Identity that owns the codes")]
        [CommandArgument(0, "<owner>")]
        public string Owner { get; init; } = "";
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = settings.CreateHttpClient();
        var response = await httpClient.GetAsync($"invitation-codes?owner={Uri.EscapeDataString(settings.Owner)}");
        await ApiResponse.EnsureAsync(response);

        var codes = await response.Content.ReadFromJsonAsync<List<InvitationCodeInfo>>() ?? [];
        codes.ForEach(InvitationCodeSettings.Write);
        return 0;
    }
}
