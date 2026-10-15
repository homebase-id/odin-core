using System.ComponentModel;
using Odin.Cli.Commands.Base;
using Odin.Services.Registry.Registration;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.InvitationCode;

// PROTOTYPE (#1908): defaults to bleeding edge and its hard-coded key
public class InvitationCodeSettings : BaseSettings
{
    [Description("Provisioning host(:port) (default: bleeding edge)")]
    [CommandOption("-P|--provisioning-host <VALUE>")]
    public string ProvisioningHost { get; init; } = InvitationCodePrototype.BleedingEdgeProvisioningDomain;

    [Description("Invitation-code key (default: the prototype key)")]
    [CommandOption("-K|--api-key <VALUE>")]
    public string ApiKey { get; init; } = InvitationCodePrototype.ApiKey;

    public HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri($"https://{ProvisioningHost}/api/registration/v1/") };
        httpClient.DefaultRequestHeaders.Add(InvitationCodePrototype.ApiKeyHeader, ApiKey);
        return httpClient;
    }
}
