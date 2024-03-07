using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Sent when a <see cref="ConnectionRequest"/> is accepted by the <see cref="ConnectionRequest.Recipient"/>
    /// to establish a connection
    /// </summary>
    public class ConnectionRequestReply
    {
        // public string SharedSecretEncryptedCredentials { get; set; }

        public ContactRequestData ContactData { get; set; }

        public string SenderOdinId { get; set; }

        /// <summary>
        /// A base64 byte array of the <see cref="ClientAccessToken"/> used by the original sending
        /// identity to authenticate to the receiving identity (to be stored in the ICR)
        /// </summary>
        public string ClientAccessTokenReply64 { get; set; }
        
        public byte[] TempKey { get; set; }
        
    }
}
