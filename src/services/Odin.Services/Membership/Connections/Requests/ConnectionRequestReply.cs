using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Sent when a <see cref="ConnectionRequest"/> is accepted by the <see cref="ConnectionRequest.Recipient"/>
    /// to establish a connection
    /// </summary>
    public class ConnectionRequestReply
    {
        public ContactRequestData ContactData { get; init; }

        public string SenderOdinId { get; init; }

        /// <summary>
        /// A base64 byte array of the <see cref="ClientAccessToken"/> used by the original sending
        /// identity to authenticate to the receiving identity (to be stored in the ICR)
        /// </summary>
        public string ClientAccessTokenReply64 { get; init; }
        
        public byte[] TempKey { get; init; }
        
        public byte[] VerificationHash { get; set; }
    }
}
