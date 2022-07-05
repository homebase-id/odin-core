using System;
using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    public class AcceptRequestHeader
    {
        public string Sender { get; set; }
        
        /// <summary>
        /// The drives which should be accessible to the recipient of this request
        /// </summary>
        public IEnumerable<TargetDrive> Drives { get; set; }
        
        /// <summary>
        /// The permissions which should be granted to the recipient
        /// </summary>
        public PermissionSet Permissions { get; set; }
    }
    
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
        /// The display name to be shown to the recipient
        /// </summary>
        public string Name { get; set; }

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
        /// The drives which should be accessible to the recipient of this request
        /// </summary>
        public IEnumerable<TargetDrive> Drives { get; set; }
        
        /// <summary>
        /// The permissions which should be granted to the recipient
        /// </summary>
        public PermissionSet Permissions { get; set; }
    }
}