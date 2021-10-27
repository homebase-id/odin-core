using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Tests.Transit
{
    public interface ITransitTestHttpClient
    {
        private const string RootPath = "/api/admin/transit";
        private const string ClientRootEndpoint = "/api/admin/transit/client";
        private const string HostRootEndpoint = "/api/admin/transit/host";
        
        [Post(HostRootEndpoint)]
        Task<ApiResponse<Guid>> SendHostToHost();
        
        [Multipart]
        [Post(ClientRootEndpoint)]
        Task<ApiResponse<TransferResult>> SendClientToHost(
            [AliasAs("recipients")] RecipientList recipientList,
            [AliasAs("hdr")] KeyHeader metadata, 
            [AliasAs("metaData")] StreamPart metaData, 
            [AliasAs("payload")] StreamPart payload);
    }
}