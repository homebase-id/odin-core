using System;
using System.IO;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Container for sending data to a recipient on it's own thread
    /// </summary>
    public class RecipientDataTransferProcess
    {
        private readonly string _recipient;
        private readonly TransferEnvelope _envelope;
        private readonly TransferResultCallback _callback;

        public RecipientDataTransferProcess(object todoTenantContext, string recipient, TransferEnvelope envelope, TransferResultCallback callback)
        {
            this._recipient = recipient;
            _envelope = envelope;
            _callback = callback;
        }

        public async Task<SendResult> Run()
        {
            TransferFailureReason tfr = TransferFailureReason.UnknownError;
            bool success = false;
            try
            {
                var recipientHeader = EncryptHeader(_recipient, _envelope.Header).ConfigureAwait(false).GetAwaiter().GetResult();

                var metaDataStream = new StreamPart(File.Open(_envelope.File.MetaDataPath, FileMode.Open), "metadata.encrypted", "application/json", "metadata");
                var payload = new StreamPart(File.Open(_envelope.File.DataFilePath, FileMode.Open), "payload.encrypted", "application/x-binary", "payload");

                //TODO: add additional error checking for files existing and successfully being opened, etc.

                var client = GetHttpClient(_recipient);
                var result = client.DeliverStream(recipientHeader, metaDataStream, payload).ConfigureAwait(false).GetAwaiter().GetResult();
                success = result.IsSuccessStatusCode;

                //TODO: add more resolution to these errors (i.e. checking for invalid recipient public key, etc.)
                if (!success)
                {
                    tfr = TransferFailureReason.RecipientServerError;
                }
            }
            catch (EncryptionException)
            {
                tfr = TransferFailureReason.CouldNotEncrypt;
                //TODO: logging
            }
            catch (Exception)
            {
                tfr = TransferFailureReason.UnknownError;
                //TODO: logging
            }
            
            return new SendResult()
            {
                Recipient = _recipient,
                Success = success,
                FailureReason = tfr,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TransferEnvelope = _envelope
            };
        }

        public void RunWithCallback()
        {
            if (null == _callback)
            {
                throw new Exception("Callback must not be null");
            }

            var recipientHeader = EncryptHeader(_recipient, _envelope.Header).ConfigureAwait(false).GetAwaiter().GetResult();
            var metaDataStream = new StreamPart(File.Open(_envelope.File.MetaDataPath, FileMode.Open), "metadata.encrypted", "application/json", "metadata");
            var payload = new StreamPart(File.Open(_envelope.File.DataFilePath, FileMode.Open), "payload.encrypted", "application/x-binary", "payload");

            var client = GetHttpClient(_recipient);
            var result = client.DeliverStream(recipientHeader, metaDataStream, payload).ConfigureAwait(false).GetAwaiter().GetResult();
            _callback(_recipient, result.IsSuccessStatusCode);
        }

        private async Task<KeyHeader> EncryptHeader(string recipient, KeyHeader originalHeader)
        {
            //TODO: handle
            // we cannot get the recipient's public key because their DI is offline (or any other reason)
            // if an error occurs during transposition, this has to go into an encryption queue to be retried

            string publicKey64 = GetRecipientPublicKey(recipient);

            if (null == publicKey64)
            {
                throw new EncryptionException("No Public Key for recipient");
            }

            //TODO: perform encryption; throw new if it fails
            return new KeyHeader()
            {
                Id = Guid.NewGuid(),
                EncryptedKey64 = Convert.ToBase64String(new byte[] { 1, 1, 2, 3, 5, 8, 13, 21 }),
            };
        }

        private string GetRecipientPublicKey(string recipient)
        {
            //TODO: plug ino service to get key. decide if this should be real time or only look at the cached version
            return "";
        }

        private IPerimeterHttpClient GetHttpClient(string dotYouId)
        {
            var client = new System.Net.Http.HttpClient();
            client.BaseAddress = new UriBuilder() { Scheme = "http", Host = dotYouId, Port = 5000 }.Uri;
            var ogClient = RestService.For<IPerimeterHttpClient>(client);
            return ogClient;
        }
    }
}