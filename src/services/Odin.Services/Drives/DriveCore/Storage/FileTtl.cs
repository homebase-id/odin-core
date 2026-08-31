using System;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Encoding of <see cref="FileMetadata.Ttl"/> — a single <see cref="long"/> in
    /// <b>milliseconds</b> carrying three behaviours:
    ///
    /// <list type="bullet">
    /// <item><c>0</c> — never expires. This is what every file written before the field existed
    /// deserializes to, so the status quo is preserved without a migration.</item>
    /// <item><c>&gt; 0</c> — an absolute <see cref="UnixTimeUtc"/> at which the file dies.</item>
    /// <item><c>&lt; 0</c> — a duration. On this copy's first payload read the field rewrites itself
    /// to <c>now() - Ttl</c> (Ttl is negative, so that is <c>now() + |Ttl|</c>) and the file dies
    /// then. Unread, it dies at <see cref="UnreadBackstop"/> after creation.</item>
    /// </list>
    ///
    /// The negative branch is a one-way door: once resolved it is indistinguishable from an absolute
    /// TTL, so every later read and the expiry job take a single code path.
    ///
    /// Milliseconds throughout because <see cref="UnixTimeUtc"/> is millisecond-precision. Prefer the
    /// <see cref="After"/> / <see cref="AfterFirstRead"/> helpers over writing raw numbers — a value
    /// handed in as seconds lands in 1970 and is rejected by upload validation.
    /// </summary>
    public static class FileTtl
    {
        /// <summary>The file never expires.</summary>
        public const long Never = 0;

        /// <summary>
        /// How long an unread <c>&lt; 0</c> (expire-after-first-read) file survives before it is
        /// deleted anyway. Without it a message nobody ever opened would live forever.
        /// </summary>
        public static readonly TimeSpan UnreadBackstop = TimeSpan.FromDays(30);

        /// <summary>
        /// How long a soft-deleted tombstone is kept before it is hard deleted. Long enough that every
        /// client has certainly synced the deletion; without the reap, the index only ever grows.
        /// </summary>
        public static readonly TimeSpan TombstoneGrace = TimeSpan.FromDays(30);

        /// <summary>Dies at an absolute point in time.</summary>
        public static long At(UnixTimeUtc when) => when.milliseconds;

        /// <summary>Dies <paramref name="lifetime"/> from now, regardless of whether anyone reads it.</summary>
        public static long After(TimeSpan lifetime) => UnixTimeUtc.Now().milliseconds + (long)lifetime.TotalMilliseconds;

        /// <summary>
        /// Dies <paramref name="lifetime"/> after the first payload read of this copy. Each identity's
        /// copy runs its own clock from its own reader's first view.
        /// </summary>
        public static long AfterFirstRead(TimeSpan lifetime) => -(long)lifetime.TotalMilliseconds;

        public static bool IsNever(long ttl) => ttl == Never;

        /// <summary>True while a <c>&lt; 0</c> TTL is still waiting for its first payload read.</summary>
        public static bool IsPendingFirstRead(long ttl) => ttl < Never;

        /// <summary>True once the TTL is an absolute point in time.</summary>
        public static bool IsAbsolute(long ttl) => ttl > Never;

        /// <summary>
        /// Resolves a <c>&lt; 0</c> TTL against the clock: <c>now() - ttl</c>, which for a negative
        /// ttl is <c>now() + |ttl|</c>.
        /// </summary>
        public static long ResolveFirstRead(long ttl, UnixTimeUtc now)
        {
            if (!IsPendingFirstRead(ttl))
            {
                throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "Only a negative TTL can be resolved");
            }

            return now.milliseconds - ttl;
        }

        public static bool HasExpired(long ttl, UnixTimeUtc now) => IsAbsolute(ttl) && ttl <= now.milliseconds;

        /// <summary>
        /// When an unread <c>&lt; 0</c> file must be deleted anyway, measured from its creation.
        /// </summary>
        public static long UnreadBackstopFor(UnixTimeUtc created) =>
            created.milliseconds + (long)UnreadBackstop.TotalMilliseconds;

        /// <summary>
        /// The point at which a file with this TTL should be soft deleted, or null if it never expires.
        /// A pending <c>&lt; 0</c> TTL answers with its unread backstop.
        /// </summary>
        public static long? ExpiresAt(long ttl, UnixTimeUtc created)
        {
            if (IsNever(ttl))
            {
                return null;
            }

            return IsAbsolute(ttl) ? ttl : UnreadBackstopFor(created);
        }

        /// <summary>
        /// True when <paramref name="candidate"/> would extend the life of a file that already has
        /// <paramref name="existing"/>. Used to enforce the shorten-only rule on update: a routine file
        /// update must never silently resurrect an expiring message.
        /// </summary>
        public static bool Extends(long candidate, long existing, UnixTimeUtc created)
        {
            if (IsNever(existing))
            {
                return false; // nothing to extend; a file that never expired may be given a TTL
            }

            if (IsNever(candidate))
            {
                return true; // clearing an existing TTL makes the file immortal
            }

            var existingAt = ExpiresAt(existing, created);
            var candidateAt = ExpiresAt(candidate, created);
            return candidateAt > existingAt;
        }

        /// <summary>
        /// The shorter-lived of two TTLs, used to enforce the shorten-only rule on update.
        ///
        /// This clamps rather than rejects on purpose. A file that crosses peer is updated by its
        /// sender, whose TTL is still the original duration, while the recipient's copy may already
        /// have resolved to a concrete time on their first read — throwing there would break ordinary
        /// delivery. Clamping keeps the invariant (an update never lengthens a life) without inventing
        /// a new failure mode.
        /// </summary>
        public static long Shortest(long candidate, long existing, UnixTimeUtc created)
            => Extends(candidate, existing, created) ? existing : candidate;
    }
}
