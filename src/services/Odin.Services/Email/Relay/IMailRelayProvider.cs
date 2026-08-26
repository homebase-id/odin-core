using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Util;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Email.Relay;

/// <summary>
/// Onboards a tenant domain with the outbound relay, so mail sent From: that domain is
/// accepted. Stalwart still receives; this only concerns sending.
///
/// Every method is safe to call again: the onboarding job retries on transient network
/// failure, and a half-completed run must converge rather than double-provision.
/// </summary>
public interface IMailRelayProvider
{
    bool IsConfigured { get; }

    /// <summary>
    /// Registers <paramref name="domain"/> for sending if it is not registered already, and
    /// returns its current state either way.
    /// </summary>
    Task<MailRelayDomainState> EnsureDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);

    /// <summary>Current state without registering anything. Null when the domain is unknown to the relay.</summary>
    Task<MailRelayDomainState?> GetDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);

    /// <summary>Asks the relay to re-check its DNS records. Returns the state after checking.</summary>
    Task<MailRelayDomainState> VerifyDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);

    /// <summary>Removes the domain. Used on tenant deletion, beside the existing DNS cleanup.</summary>
    Task RemoveDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the relay says about one tenant domain, in our own vocabulary rather than a vendor's.
/// </summary>
public class MailRelayDomainState
{
    public string Domain { get; init; } = "";

    /// <summary>The DNS records the tenant's zone must carry for the relay to accept its mail.</summary>
    public List<DnsConfig> Records { get; init; } = [];

    /// <summary>True once every record in <see cref="Records"/> is confirmed by the relay.</summary>
    public bool Verified { get; init; }

    /// <summary>
    /// Per-record diagnostics from the relay, verbatim - far more useful on a status page than
    /// a bare boolean ("Lookup CNAME(...) failed ... NXDOMAIN" tells the owner what to fix).
    /// </summary>
    public List<string> Problems { get; init; } = [];
}
