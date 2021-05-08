using System;
using Dawn;

namespace DotYou.Types.Circle
{
    /// <summary>
    /// Sent when a <see cref="ConnectionRequest"/> is accepted by the <see cref="ConnectionRequest.Recipient"/> 
    /// to establish a connection
    /// </summary>
    public class EstablishConnectionRequest: IRequireSenderCertificate
    {
        /// <summary>
        /// The Id of the original connection request
        /// </summary>
        public Guid ConnectionRequestId { get; set; }
       
        public string RecipientGivenName { get; set; }
        
        public string RecipientSurname { get; set; }
        
        public string SenderPublicKeyCertificate { get; set; }
        
        public void Validate()
        {
            Guard.Argument(ConnectionRequestId, nameof(ConnectionRequestId)).NotEqual(Guid.Empty);
            Guard.Argument(SenderPublicKeyCertificate, nameof(SenderPublicKeyCertificate)).NotEmpty();
            Guard.Argument(RecipientGivenName, nameof(RecipientGivenName)).NotEmpty();
            Guard.Argument(RecipientSurname, nameof(RecipientSurname)).NotEmpty();
        }

    }
}
