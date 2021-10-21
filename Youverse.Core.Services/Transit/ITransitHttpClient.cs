using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitHttpClient
    {
        private const string RootPath = "/api/perimeter";
        
        [Multipart]
        [Post("/data/datastream")]
        Task<ApiResponse<bool>> DeliverStream(
            [AliasAs("hdr")] KeyHeader metadata, 
            [AliasAs("metaData")] StreamPart metaData, 
            [AliasAs("payload")] StreamPart payload);
    }
}