using System;
using System.Collections.Generic;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// What actually happened to each circle the owner chose during a review.
    /// </summary>
    /// <remarks>
    /// The review dialog closes as soon as the call returns, so the outcome has to come back with it --
    /// otherwise the client can only learn what took effect on a later status read, by which point the
    /// screen it needed to render is gone.
    /// <para>
    /// Three outcomes, not two.  <see cref="Granted"/> is the only one where the contact can decrypt
    /// anything right now; a client showing "done" for the other two would be claiming access that does
    /// not exist yet, which is the exact failure the pending state exists to prevent.
    /// </para>
    /// </remarks>
    public class ReviewConnectionResult
    {
        /// <summary>
        /// Minted as a live circle grant.  The contact holds it now.
        /// </summary>
        public List<Guid> Granted { get; init; } = [];

        /// <summary>
        /// Sealed to the connection's write-only key, awaiting conversion.
        /// </summary>
        /// <remarks>
        /// An app holds no master key, so even a circle it owns is deposited rather than minted; it
        /// becomes a real grant the next time the connection's key store key is in scope -- peer CAT auth,
        /// or the owner's next grant touch.  Not yet usable, so group this with <see cref="Pending"/>
        /// rather than with <see cref="Granted"/> when the UI has only two states.
        /// </remarks>
        public List<Guid> Deposited { get; init; } = [];

        /// <summary>
        /// Recorded but not acted on, because the circle belongs to an app whose keys the reviewing
        /// client does not hold.  That app completes it the next time it runs.
        /// </summary>
        /// <remarks>
        /// Always empty until the cross-app pending queue lands; the shape is here so the client does not
        /// have to change when it does.
        /// </remarks>
        public List<Guid> Pending { get; init; } = [];

        /// <summary>
        /// When the review was recorded.  Null only if the connection was already reviewed and keeps its
        /// original stamp.
        /// </summary>
        public Odin.Core.Time.UnixTimeUtc? ReviewedAt { get; init; }
    }
}
