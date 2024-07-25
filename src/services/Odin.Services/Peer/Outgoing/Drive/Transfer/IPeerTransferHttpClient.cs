using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.Cmp;
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

        [Post(DriveRoot + "/deletelinkedfile")]
        Task<ApiResponse<PeerTransferResponse>> DeleteLinkedFile([Body] DeleteRemoteFileRequest request);

        [Post(DriveRoot + "/mark-file-read")]
        Task<ApiResponse<PeerTransferResponse>> MarkFileAsRead(MarkFileAsReadRequest markFileAsReadRequest);

        [Multipart]
        [Post(DriveRoot + "/update-payloads")]
        Task<ApiResponse<PeerTransferResponse>> UpdatePayloads(
            StreamPart instructionSet,
            params StreamPart[] additionalStreamParts);

        [Post(DriveRoot + "/delete-payloads")]
        Task<ApiResponse<PeerTransferResponse>> DeletePayload([Body] DeleteRemotePayloadRequest request);
    }
}