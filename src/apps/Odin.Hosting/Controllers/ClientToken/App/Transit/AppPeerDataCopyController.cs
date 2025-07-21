using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Peer.DataCopy;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.PeerDataCopyV1)]
    [AuthorizeValidAppToken]
    public class AppPeerDataCopyController(PeerDataCopyService dataCopyService) : OdinControllerBase
    {
        [HttpPost("copy")]
        public async Task<IActionResult> CopyDataFromPeer(CopyPeerDataRequest request)
        {
            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            var localFile = await dataCopyService.CopyFile(
                request.RemoteIdentity,
                request.SourceFileIdentifier, 
                request.LocalDrive,
                fst,
                request.Overwrite,
                WebOdinContext);

            return Ok(localFile);
        }
    }
}