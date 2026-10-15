using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using Odin.Cli.Commands.Base;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.InvitationCode;

[Description("Create an invitation code owned by an identity, usable a given number of times")]
public sealed class CreateInvitationCodeCommand : AsyncCommand<CreateInvitationCodeCommand.Settings>
{
    public sealed class Settings : BaseSettings
    {
        [Description("Identity that owns the code")]
        [CommandArgument(0, "<owner>")]
        public string Owner { get; init; } = "";

        [Description("How many signups the code allows")]
        [CommandOption("-u|--uses <VALUE>")]
        public int Uses { get; init; } = 5;

        [Description("Invitation-code service URL (alt: set env var ODIN_INVITATION_CODE_SERVICE_URL)")]
        [CommandOption("-S|--service-url <VALUE>")]
        public string ServiceUrl { get; set; } = "";

        [Description("Create API key (alt: set env var ODIN_INVITATION_CODE_CREATE_API_KEY)")]
        [CommandOption("-K|--api-key <VALUE>")]
        public string ApiKey { get; set; } = "";

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(ServiceUrl))
            {
                ServiceUrl = Environment.GetEnvironmentVariable("ODIN_INVITATION_CODE_SERVICE_URL") ?? "";
            }
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                ApiKey = Environment.GetEnvironmentVariable("ODIN_INVITATION_CODE_CREATE_API_KEY") ?? "";
            }

            if (ServiceUrl == "")
            {
                return ValidationResult.Error("Invitation-code service URL (-S) is required");
            }
            if (ApiKey == "")
            {
                return ValidationResult.Error("Create API key (-K) is required");
            }
            if (Uses < 1)
            {
                return ValidationResult.Error("Uses (-u) must be at least 1");
            }
            return base.Validate();
        }
    }

    private record CreateRequest(string Owner, int MaxUses);
    private record CodeResponse(string Code, string Owner, int MaxUses, int UsesLeft);

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri(settings.ServiceUrl.TrimEnd('/') + "/") };
        httpClient.DefaultRequestHeaders.Add("Odin-Api-Key", settings.ApiKey);

        var response = await httpClient.PostAsJsonAsync("api/v1/invitation-codes/",
            new CreateRequest(settings.Owner, settings.Uses));
        await ApiResponse.EnsureAsync(response);

        var created = await response.Content.ReadFromJsonAsync<CodeResponse>()
                      ?? throw new Exception("The invitation-code service sent an empty answer");
        AnsiConsole.MarkupLineInterpolated($"{created.Code} owner={created.Owner} uses={created.MaxUses}");
        return 0;
    }
}
