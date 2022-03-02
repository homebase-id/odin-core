using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Exchange;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    public class ConnectionInfo : DotYouIdBase
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

        public ExchangeRegistration ExchangeRegistration { get; set; }
        
        public byte[] RemoteGrantKey { get; set; }

        public byte[] RemoteSharedSecret { get; set; }

        public long LastUpdated { get; set; }
    }
}