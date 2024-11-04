using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Identity;

namespace Odin.Services.Membership.Connections.Requests
{
    public class ConnectionRequestHeader
    {
        private string _recipient;

        public Guid Id
        {
            get => (OdinId)_recipient;
            set
            {
                //TODO: review
                //no-op as the Id is based on the odinId of the recipient.  this is wierd
            }
        }

        /// <summary>
        /// Initial data sent with a connection request
        /// </summary>
        public ContactRequestData ContactData { get; set; }

        /// <summary>
        /// Individual receiving the invite
        /// </summary>
        public string Recipient
        {
            get => _recipient;
            set => _recipient = value;
        }

        /// <summary>
        /// Text to be sent with the invite explaining why you should connect with me.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The circles to be granted to the recipient
        /// </summary>
        public List<GuidId> CircleIds { get; set; }

        /// <summary>
        /// The identity who introduced the recipient to the sender
        /// </summary>
        public OdinId? IntroducerOdinId { get; init; }

        public ConnectionRequestOrigin ConnectionRequestOrigin { get; init; } = ConnectionRequestOrigin.IdentityOwner;
    }
}