#nullable enable
using System.Threading.Tasks;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Owner-scoped sync facade. Routes <see cref="ITestSync.ProcessInboxAsync"/> through the owner's
/// V1 endpoint at <c>/api/owner/v1/transit/inbox/processor/process</c>; everything else delegates
/// to the host's direct <see cref="ITestSync"/>. See <see cref="HttpInboxSync{TRefit}"/> for the
/// reasoning behind the HTTP route.
/// </summary>
public sealed class OwnerSync : HttpInboxSync<OwnerSync.IOwnerInboxRefit>
{
    internal OwnerSync(ITestSync hostSync, OwnerSession owner) : base(hostSync, owner)
    {
    }

    protected override Task<ApiResponse<InboxStatus>> CallProcessInboxAsync(
        IOwnerInboxRefit svc, ProcessInboxRequest request)
        => svc.ProcessInbox(request);

    public interface IOwnerInboxRefit
    {
        [Post("/transit/inbox/processor/process")]
        Task<ApiResponse<InboxStatus>> ProcessInbox([Body] ProcessInboxRequest request);
    }
}
