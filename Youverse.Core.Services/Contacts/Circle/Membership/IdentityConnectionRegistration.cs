using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Specifies that an identity shares a connection with another identity (i.e. friend request)
    /// </summary>
    public class IdentityConnectionRegistration : DotYouIdBase
    {
        private ConnectionStatus _status;

        public ConnectionStatus Status
        {
            get { return _status; }
            set
            {
                _status = value;
                this.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        public bool IsConnected()
        {
            return this._status == ConnectionStatus.Connected;
        }
        
        /// <summary>
        /// The Id to the access registration which defines this connection's access levels
        /// </summary>
        public Guid AccessRegistrationId { get; set; }

        public byte[] ClientAccessTokenHalfKey { get; set; }
        
        public byte[] ClientAccessTokenSharedSecret { get; set; }
        
        public long LastUpdated { get; set; }
    }
}