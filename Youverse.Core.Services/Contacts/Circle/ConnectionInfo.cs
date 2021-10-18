using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Contacts.Circle
{
    public class ConnectionInfo: DotYouIdBase
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

        public long LastUpdated { get; set; }
    }
}