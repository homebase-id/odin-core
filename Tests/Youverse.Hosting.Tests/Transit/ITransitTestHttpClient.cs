using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Tests.Transit
{
    public interface ITransitTestHttpClient
    {
        private const string ClientRootEndpoint = "/api/transit/client";
        
        [Multipart]
        [Post(ClientRootEndpoint + "/sendparcel")]
        Task<ApiResponse<TransferResult>> SendClientToHost(
            [AliasAs("recipients")] RecipientList recipientList,
            [AliasAs("header")] KeyHeader metadata,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);
    }
}