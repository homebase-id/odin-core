using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Email;

public interface IEmailSender
{
    Task SendAsync(Envelope envelope);

    /// <summary>
    /// Cheap provider-credentials sanity check used by the startup verifier.
    /// Providers without a meaningful check (e.g. NullEmailSender) inherit the default.
    /// </summary>
    Task<bool> VerifyCredentialsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}