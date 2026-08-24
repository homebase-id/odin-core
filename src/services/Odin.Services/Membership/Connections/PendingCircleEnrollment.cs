using System;
using Odin.Core.Time;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// A circle the owner chose during a review that the reviewing client could not mint.
    /// </summary>
    /// <remarks>
    /// The client holds App Keys for its own suite only.  A checked toggle for any other app -- mail
    /// reviewed from the chat client, a third-party vendor app -- cannot enrol on the spot: minting a
    /// read-bearing grant needs the storage key of a drive the caller cannot reach, and
    /// <c>CreateDepositedGrantAsync</c> throws rather than mint a keyless one.
    /// <para>
    /// So the decision is recorded instead of discarded, and the owning app completes it the next time it
    /// runs with its own App Key.  Until then the client shows that app's toggle as <i>pending</i> -- the
    /// dialog never claims access that does not exist yet.
    /// </para>
    /// <para>
    /// This rides the connection registration record; no new schema.
    /// </para>
    /// </remarks>
    public class PendingCircleEnrollment
    {
        /// <summary>
        /// The circle the owner asked for.
        /// </summary>
        public Guid CircleId { get; set; }

        /// <summary>
        /// The app that owns the circle, and therefore the only one that can complete this.
        /// </summary>
        public Guid AppId { get; set; }

        /// <summary>
        /// When the owner made the decision -- not when it took effect.
        /// </summary>
        public UnixTimeUtc RequestedAt { get; set; }
    }
}
