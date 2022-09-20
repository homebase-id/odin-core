using System;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    public class ConnectionRequestHeader
    {
        private string _recipient;

        public Guid Id
        {
            get => (DotYouIdentity)_recipient;
            set
            {
                //TODO: review
                //no-op as the Id is based on the dotYouId of the recipient.  this is wierd
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
        /// The circles 
        /// </summary>
        public List<GuidId> CircleIds { get; set; }
    }
}