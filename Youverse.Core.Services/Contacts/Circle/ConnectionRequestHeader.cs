using System;
using Newtonsoft.Json;
using Youverse.Core.Identity;

namespace DotYou.Types.Circle
{
    public class ConnectionRequestHeader
    {
        private DotYouIdentity _recipient;

        public Guid Id
        {
            get => _recipient;
            set
            {
                //TODO: review
                //no-op as the Id is based on the dotYouId of the recpient.  this is wierd
            }
        }

        /// <summary>
        /// Individual receiving the invite
        /// </summary>
        public DotYouIdentity Recipient
        {
            get => _recipient;
            set => _recipient = value;
        }

        /// <summary>
        /// Text to be sent with the invite explaining why you should connect with me.
        /// </summary>
        public string Message { get; set; }
    }
}