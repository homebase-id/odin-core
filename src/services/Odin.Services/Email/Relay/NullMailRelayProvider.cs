using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Services.Email.Relay;

/// <summary>
/// No outbound relay configured — the shipped default.
///
/// <see cref="IsConfigured"/> is false, so callers skip relay work entirely; the throwing
/// methods exist to catch a caller that ignored the flag rather than to be handled. Silently
/// returning an empty state would let a tenant look onboarded when nothing happened.
/// </summary>
public class NullMailRelayProvider : IMailRelayProvider
{
    public bool IsConfigured => false;

    public Task<MailRelayDomainState> EnsureDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
        => throw new OdinSystemException("No mail relay is configured (Email:Relay:Provider)");

    public Task<MailRelayDomainState?> GetDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
        => Task.FromResult<MailRelayDomainState?>(null);

    public Task<MailRelayDomainState> VerifyDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
        => throw new OdinSystemException("No mail relay is configured (Email:Relay:Provider)");

    public Task RemoveDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
