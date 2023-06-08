using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.Anonymous.Ident
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IIdentHttpClient
    {
        [Get(YouAuthApiPathConstants.AuthV1 + "/ident")]
        Task<ApiResponse<GetIdentResponse>> GetIdent();
    }
}