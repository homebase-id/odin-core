using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Hosting.Tests.Transit
{
    public interface ITransitTestHttpClient
    {
        private const string RootPath = "/api/admin/transit";
        private const string ClientRootPath = "/api/admin/transit/client";
        private const string HostRootPath = "/api/admin/transit/host";

        [Post(ClientRootPath)]
        Task<ApiResponse<PagedResult<AppRegistration>>> UploadFromClient();

        [Post(HostRootPath)]
        Task<ApiResponse<Guid>> SendHostToHost();

    }
}