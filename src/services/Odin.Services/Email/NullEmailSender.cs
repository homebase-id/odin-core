using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Odin.Services.Email;

/// <summary>
/// Registered when Email:Provider is "None" so an IEmailSender always resolves.
/// Logs and discards. Policy decisions (e.g. whether recovery mode may be entered)
/// must gate on EmailSection.IsProviderConfigured, never on reaching this sender.
/// </summary>
public class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public Task SendAsync(Envelope envelope)
    {
        logger.LogInformation(
            "Email provider is 'None'; discarding email to {to} with subject \"{subject}\"",
            string.Join(",", envelope.To.Select(x => x.Email)), envelope.Subject);
        return Task.CompletedTask;
    }
}
