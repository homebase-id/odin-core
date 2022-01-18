using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Contacts.Circle
{
    /// <summary>
    /// Metadata describing a message or payload from an external source (i.e another digital identity server or similar)
    /// </summary>
    public interface IIncomingCertificateMetaData
    {
        /// <summary>
        /// Specifies the public key certificate of the <see cref="DotYouIdentity"/> who sent this message
        /// </summary>
        // string SenderPublicKeyCertificate { get; set; }

        /// <summary>
        /// The sender's <see cref="DotYouIdentity"/>.
        /// </summary>
        DotYouIdentity SenderDotYouId { get; set; }

        /// <summary>
        /// Epoc timestamp when the message was received
        /// </summary>
        Int64 ReceivedTimestampMilliseconds { get; set; }
    }
}