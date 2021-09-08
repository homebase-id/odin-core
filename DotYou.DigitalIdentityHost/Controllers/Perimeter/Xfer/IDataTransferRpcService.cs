using System;
using MagicOnion;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter.Xfer
{
    /// <summary>
    /// Handles server-to-server communications for media data transfer
    /// </summary>
    public interface IDataTransferRpcService : IService<IDataTransferRpcService>
    {
        /// <summary>
        /// Transfer the media file and returns the recipient's id for the media file
        /// </summary>
        /// <returns>True if successful; otherwise false</returns>
        UnaryResult<Reply> Deliver(Envelope envelope);
    }
}