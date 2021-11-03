using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitHostToHostHttpClient
    {
        private const string HostRootEndpoint = "/api/perimeter/transit/host";
        
        [Multipart]
        [Post(HostRootEndpoint)]
        Task<ApiResponse<bool>> SendHostToHost(
            [AliasAs("header")] KeyHeader metadata, 
            [AliasAs("metaData")] StreamPart metaData, 
            [AliasAs("payload")] StreamPart payload);
    }
}