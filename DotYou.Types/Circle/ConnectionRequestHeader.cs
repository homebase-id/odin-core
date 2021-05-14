using System;
using Newtonsoft.Json;

namespace DotYou.Types.Circle
{
    public class ConnectionRequestHeader
    {
        [JsonConstructor]
        public ConnectionRequestHeader() { }
        
        
        public Guid Id { get; set; }
        
        /// <summary>
        /// Individual receiving the invite
        /// </summary>
        public DotYouIdentity Recipient { get; set; }
        
        /// <summary>
        /// Text to be sent with the invite explaining why you should connect with me.
        /// </summary>
        public string Message { get; set; }
        
    }
}