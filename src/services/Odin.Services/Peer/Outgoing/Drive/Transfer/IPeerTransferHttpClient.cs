using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Peer.Outgoing.DataRequestService;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    /// <summary>
    /// The interface for transferring files and deletes to peer identities 
    /// </summary>
    public interface IPeerTransferHttpClient
    {
        private const string DriveRoot = PeerApiPathConstants.DriveV1;

        [Multipart]
        [Post(DriveRoot + "/upload")]
        Task<ApiResponse<PeerTransferResponse>> SendHostToHost(
            StreamPart header,
            StreamPart metaData,
            params StreamPart[] additionalStreamParts);

        [Multipart]
        [Patch(DriveRoot + "/update")]
        Task<ApiResponse<PeerTransferResponse>> UpdatePeerFile(
            StreamPart header,
            StreamPart metaData,
            params StreamPart[] additionalStreamParts);
        
        [Post(DriveRoot + "/deletelinkedfile")]
        Task<ApiResponse<PeerTransferResponse>> DeleteLinkedFile([Body] DeleteRemoteFileRequest request);

        [Post(DriveRoot + "/mark-file-read")]
        Task<ApiResponse<PeerTransferResponse>> MarkFileAsRead(MarkFileAsReadRequest markFileAsReadRequest);
        
        [Post(DriveRoot + "/request-file")]
        Task<ApiResponse<HttpContent>> RequestRemoteFile(RemoteFileRequest request);
    }
}