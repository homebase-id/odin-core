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
        /// 
        /// Generated from RSA.ExportSubjectPublicKeyInfo #prototrial
        /// </summary>
        public string RecipientRSAPublicKeyInfoBase64 { get; set; }

        public string RecipientGivenName { get; set; }
        
        public string RecipientSurname { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(RecipientRSAPublicKeyInfoBase64) ||string.IsNullOrWhiteSpace(RecipientRSAPublicKeyInfoBase64))
            {
                throw new InvalidDataException("Accepted Connection Request is invalid");
            }
        }
    }
}
