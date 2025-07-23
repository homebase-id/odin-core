using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Peer.Outgoing.DataRequestService;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.PeerDataRequestV1)]
    [AuthorizeValidAppToken]
    public class AppPeerDataCopyController(DataRequestService dataRequestService) : OdinControllerBase
    {
        [HttpPost("send")]
        public async Task<IActionResult> CopyDataFromPeer(PeerFileRequest request)
        {
            // 
            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            await dataRequestService.RequestRemoteFile(
                request.RemoteIdentity,
                request.SourceFileIdentifier,
                request.LocalDrive,
                fst,
                request.Overwrite,
                WebOdinContext);

            return Ok();
        }
    }
}