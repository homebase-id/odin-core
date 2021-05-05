using System;

namespace DotYou.Types.TrustNetwork
{
    /// <summary>
    /// Sent when a <see cref="ConnectionRequest"/> is accepted by the <see cref="ConnectionRequest.Recipient"/> 
    /// to establish a connection
    /// </summary>
    public class EstablishConnectionRequest
    {
        /// <summary>
        /// The Id of the original connection request
        /// </summary>
        public Guid ConnectionRequestId { get; set; }
        /// <summary>
        /// The public key certificate of the <see cref="ConnectionRequest.Recipient"/> in string format.
        /// </summary>
        public string RecipientPublicKey { get; set; }

        public string RecipientGivenName { get; set; }
        public string RecipientSurname { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(RecipientPublicKey) ||string.IsNullOrWhiteSpace(RecipientPublicKey))
            {
                throw new InvalidDataException("Accepted Connection Request is invalid");
            }
        }
    }
}
