using System;
using System.Diagnostics;
using System.Linq;

namespace DotYou.Types.Circle
{
    /// <summary>
    /// Indicates implementing class needs the sender's public key certificate
    /// </summary>
    public interface IRequireSenderCertificate
    {
        /// <summary>
        /// Specifies the pubilc key certificate of the <see cref="DotYouIdentity"/> who sent this message
        /// </summary>
        string SenderPublicKeyCertificate { get; set; }

        /// <summary>
        /// The sender's <see cref="DotYouIdentity"/>.
        /// </summary>
        DotYouIdentity SenderDotYouId { get; set; }
    }
}