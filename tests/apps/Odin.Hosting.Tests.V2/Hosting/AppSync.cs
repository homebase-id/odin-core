#nullable enable
using System.Threading.Tasks;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// App-scoped sync facade. Routes <see cref="ITestSync.ProcessInboxAsync"/> through the app's
/// V1 endpoint at <c>/api/apps/v1/transit/inbox/processor/process</c>; everything else delegates
/// to the host's direct <see cref="ITestSync"/>. See <see cref="HttpInboxSync{TRefit}"/> for the
/// reasoning behind the HTTP route.
/// </summary>
public sealed class AppSync : HttpInboxSync<AppSync.IAppInboxRefit>
{
    internal AppSync(ITestSync hostSync, AppSession app) : base(hostSync, app)
    {
    }

    protected override Task<ApiResponse<InboxStatus>> CallProcessInboxAsync(
        IAppInboxRefit svc, ProcessInboxRequest request)
        => svc.ProcessInbox(request);

    public interface IAppInboxRefit
    {
        // Absolute path — V1PathNormalizingHandler only prefixes owner paths.
        [Post("/api/apps/v1/transit/inbox/processor/process")]
        Task<ApiResponse<InboxStatus>> ProcessInbox([Body] ProcessInboxRequest request);
    }
}
