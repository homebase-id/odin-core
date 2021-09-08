using System;
using MagicOnion;
using MagicOnion.Server;
using Microsoft.Extensions.Logging;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter.Xfer
{
    //Note: class is dynamically instantiated by Magic Onion
    public class DataTransferRpcService : ServiceBase<IDataTransferRpcService>, IDataTransferRpcService
    {
        ILogger _logger;

        public DataTransferRpcService(ILogger logger)
        {
            _logger = logger;
        }

        public UnaryResult<Reply> Deliver(Envelope envelope)
        {
            try
            {
                //decrypt payload type
                var payloadType = DecryptPayloadType(envelope.PayloadType);

                //route to service based on payload type -
                

                return new UnaryResult<Reply>(new Reply()
                {
                    Success = true
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to handle delivery");
                return new UnaryResult<Reply>(new Reply()
                {
                    Success = false,
                    FailureReason = FailureReason.InternalServerError
                });
            }
        }

        private string DecryptPayloadType(byte[] payloadBytes)
        {
            //TODO: add decryption
            return System.Text.Encoding.ASCII.GetString(payloadBytes);
        }
    }
}