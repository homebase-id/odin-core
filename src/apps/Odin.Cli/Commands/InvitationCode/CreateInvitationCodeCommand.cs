using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using Odin.Cli.Commands.Base;
using Odin.Services.Registry.Registration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.InvitationCode;

[Description("Create an invitation code owned by an identity, usable a given number of times")]
public sealed class CreateInvitationCodeCommand : AsyncCommand<CreateInvitationCodeCommand.Settings>
{
    public sealed class Settings : InvitationCodeSettings
    {
        [Description("Identity that owns the code")]
        [CommandArgument(0, "<owner>")]
        public string Owner { get; init; } = "";

        [Description("How many signups the code allows")]
        [CommandOption("-u|--uses <VALUE>")]
        public int Uses { get; init; } = 5;

        public override ValidationResult Validate()
        {
            return Uses < 1 ? ValidationResult.Error("Uses (-u) must be at least 1") : base.Validate();
        }
    }

    private record CreateRequest(string Owner, int MaxUses);

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = settings.CreateHttpClient();
        var response = await httpClient.PostAsJsonAsync("invitation-codes", new CreateRequest(settings.Owner, settings.Uses));
        await ApiResponse.EnsureAsync(response);

        var created = await response.Content.ReadFromJsonAsync<InvitationCodeInfo>()
                      ?? throw new Exception("The provisioning host sent an empty answer");
        InvitationCodeSettings.Write(created);
        return 0;
    }
}
