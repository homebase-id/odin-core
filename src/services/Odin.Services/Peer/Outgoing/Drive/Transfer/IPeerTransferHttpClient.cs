using System.Threading;
using System.Threading.Tasks;
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
            StreamPart[] additionalStreamParts,
            CancellationToken cancellationToken = default);

        [Multipart]
        [Patch(DriveRoot + "/update")]
        Task<ApiResponse<PeerTransferResponse>> UpdatePeerFile(
            StreamPart header,
            StreamPart metaData,
            StreamPart[] additionalStreamParts,
            CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/deletelinkedfile")]
        Task<ApiResponse<PeerTransferResponse>> DeleteLinkedFile([Body] DeleteRemoteFileRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/mark-file-read")]
        Task<ApiResponse<PeerTransferResponse>> MarkFileAsRead(MarkFileAsReadRequest markFileAsReadRequest, CancellationToken cancellationToken = default);
    }
}