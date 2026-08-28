#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Odin.Core.Exceptions;
using Odin.Services.Base;

namespace Odin.Services.Drives.Management
{
    /// <summary>
    /// Assigns a <c>DriveSlug</c> to drives that predate the column, and to drives created without one.
    /// </summary>
    /// <remarks>
    /// The drive counterpart of <see cref="Odin.Services.Authorization.Apps.AppSlugGenerator"/>, and
    /// deliberately the same shape.  A slug is a URL path segment and a wire address that other identities
    /// resolve against, so it is immutable once written.  Normal drive creation validates a caller-supplied
    /// slug and rejects a bad one; there is nobody to reject here, so this derives one instead.
    /// <para>
    /// The known system drives get the obvious name.  Everything else is derived from the drive's name,
    /// which is the only human-meaningful thing on the record -- user-created channel drives are the case
    /// that matters, since there are arbitrarily many of them and none can be listed here.  Names collide
    /// and can slugify to nothing, so the whole set is resolved and checked up front by
    /// <see cref="GenerateAll"/>: nothing is written until every drive has a valid, unique slug.
    /// </para>
    /// <para>
    /// <b>Uniqueness is per app, and the caller scopes it.</b>  The constraint is
    /// <c>UNIQUE(identityId, AppId, DriveSlug)</c> -- one <c>news</c> per app, so <c>feed/news</c> and
    /// <c>chat/news</c> may coexist (<c>docs/drive-addressing.md</c>).  So <paramref name="taken"/> must
    /// hold the slugs used by <b>the owning app</b>, not by the whole identity: a wider set would push the
    /// second app's drive to <c>news-2</c>, a permanent address nobody asked for, for a collision the
    /// schema allows.
    /// </para>
    /// </remarks>
    public static class DriveSlugGenerator
    {
        public const int MaxLength = OdinSlug.MaxLength;

        private static readonly Regex ValidSlug = new("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);
        private static readonly Regex NonSlugRun = new("[^a-z0-9]+", RegexOptions.Compiled);

        /// <summary>
        /// Slugs for the drives that ship with every identity, keyed by drive alias.  These are the
        /// addresses the routes assume (<c>/apps/chat/drives/chat</c>), so they are assigned rather than
        /// derived.
        /// </summary>
        /// <remarks>
        /// Covers every entry in <see cref="SystemDriveConstants.SystemDrives"/>.  A drive added there
        /// without a line here still works -- it just falls through to derivation and gets whatever its
        /// name slugifies to, which is very likely not the address anyone intended.
        /// </remarks>
        public static readonly IReadOnlyDictionary<Guid, string> KnownDriveSlugs = new Dictionary<Guid, string>
        {
            [WellKnownAppDrives.ChatDrive.Alias.Value] = "chat",
            [WellKnownAppDrives.StickerDrive.Alias.Value] = "stickers",
            [WellKnownAppDrives.CommunityDrive.Alias.Value] = "community",
            [WellKnownAppDrives.ContactDrive.Alias.Value] = "contacts",
            [WellKnownAppDrives.ProfileDrive.Alias.Value] = "profile",
            [WellKnownAppDrives.EmailAppDrive.Alias.Value] = "email",
            [WellKnownAppDrives.FeedDrive.Alias.Value] = "feed",
            [WellKnownAppDrives.PublicPostsChannelDrive.Alias.Value] = "posts",
            [WellKnownAppDrives.HomePageConfigDrive.Alias.Value] = "home",
            [WellKnownAppDrives.ListsDrive.Alias.Value] = "lists",
            [WellKnownAppDrives.LocationDrive.Alias.Value] = "location",
            [WellKnownAppDrives.MailDrive.Alias.Value] = "mail",
            [WellKnownAppDrives.MomentsDrive.Alias.Value] = "moments",
            [WellKnownAppDrives.ShardRecoveryDrive.Alias.Value] = "shard-recovery",
            [WellKnownAppDrives.WalletDrive.Alias.Value] = "wallet",
            [WellKnownAppDrives.PhotoLibraryDrive.Alias.Value] = "photos",
            [WellKnownAppDrives.VaultDrive.Alias.Value] = "vault",
            [SystemDriveConstants.TransientTempDrive.Alias.Value] = "transient"
        };

        /// <summary>
        /// Type slugs, keyed by <b>drive alias</b> rather than by drive type Guid.
        /// </summary>
        /// <remarks>
        /// Keyed per drive because the supplied mapping is not one-to-one with the type Guid: Profile,
        /// Wallet and HomePageConfig all carry type <c>5972...</c> but are given <c>profile</c>,
        /// <c>wallet</c> and <c>profile</c>.  <c>docs/drive-addressing.md</c> requires one type slug per
        /// type Guid, so this is stored as given and the conflict is recorded rather than resolved here.
        /// <para>
        /// <see cref="TypeSlugFor"/> falls back to the drive type for channel drives, which a user creates
        /// at will and which therefore cannot be listed by alias.
        /// </para>
        /// </remarks>
        public static readonly IReadOnlyDictionary<Guid, string> KnownDriveTypeSlugs = new Dictionary<Guid, string>
        {
            [WellKnownAppDrives.ChatDrive.Alias.Value] = "chat",
            [WellKnownAppDrives.StickerDrive.Alias.Value] = "sticker",
            [WellKnownAppDrives.CommunityDrive.Alias.Value] = "community",
            [WellKnownAppDrives.ContactDrive.Alias.Value] = "contact",
            [WellKnownAppDrives.ProfileDrive.Alias.Value] = "profile",
            [WellKnownAppDrives.EmailAppDrive.Alias.Value] = "email",
            [WellKnownAppDrives.FeedDrive.Alias.Value] = "feed",
            [WellKnownAppDrives.PublicPostsChannelDrive.Alias.Value] = "channel",
            [WellKnownAppDrives.HomePageConfigDrive.Alias.Value] = "profile",
            [WellKnownAppDrives.ListsDrive.Alias.Value] = "list",
            [WellKnownAppDrives.LocationDrive.Alias.Value] = "location",
            [WellKnownAppDrives.MailDrive.Alias.Value] = "mail",
            [WellKnownAppDrives.MomentsDrive.Alias.Value] = "list",
            [WellKnownAppDrives.ShardRecoveryDrive.Alias.Value] = "shard-recovery",
            [WellKnownAppDrives.WalletDrive.Alias.Value] = "profile",
            [WellKnownAppDrives.PhotoLibraryDrive.Alias.Value] = "photos",
            [WellKnownAppDrives.VaultDrive.Alias.Value] = "vault",
            [SystemDriveConstants.TransientTempDrive.Alias.Value] = "transient"
        };

        /// <summary>
        /// The readable form of a drive's type, or null when the drive is not one of the known ones and
        /// its type is not one that can be matched.
        /// </summary>
        public static string? TypeSlugFor(Guid driveId, Guid driveType)
        {
            if (KnownDriveTypeSlugs.TryGetValue(driveId, out var slug))
            {
                return slug;
            }

            // User-created channel drives: arbitrary alias, shared type.
            return driveType == SystemDriveConstants.ChannelDriveType ? "channel" : null;
        }

        /// <summary>
        /// Resolves a slug for every drive in one pass, so a collision is found before anything is written.
        /// </summary>
        /// <exception cref="OdinSystemException">
        /// If a unique, valid slug cannot be produced for some drive.  Failing here leaves the source data
        /// untouched, which is the point of doing this up front.
        /// </exception>
        public static Dictionary<Guid, string> GenerateAll(IEnumerable<(Guid DriveId, string? Name)> drives)
        {
            var result = new Dictionary<Guid, string>();
            var taken = new HashSet<string>(StringComparer.Ordinal);

            // Known drives first: their slugs are fixed, so a derived slug must yield to them rather than
            // the other way round.
            var ordered = drives
                .OrderByDescending(d => KnownDriveSlugs.ContainsKey(d.DriveId))
                .ThenBy(d => d.DriveId)
                .ToList();

            foreach (var (driveId, name) in ordered)
            {
                if (result.ContainsKey(driveId))
                {
                    throw new OdinSystemException($"Duplicate drive id {driveId} while assigning slugs");
                }

                var slug = Resolve(driveId, name, taken);

                result[driveId] = slug;
                taken.Add(slug);
            }

            return result;
        }

        /// <summary>
        /// Picks a slug for one drive, avoiding everything in <paramref name="taken"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="taken"/> is the slugs held by <b>the owning app</b> -- see the note on
        /// per-app uniqueness above.  Pass the slugs actually stored, not re-derived ones: a drive whose
        /// stored slug is <c>photos-2</c> must still be treated as holding <c>photos-2</c>, not whatever
        /// its name would slugify to today.
        /// </remarks>
        public static string Generate(Guid driveId, string? name, ISet<string> taken)
        {
            return Resolve(driveId, name, taken);
        }

        private static string Resolve(Guid driveId, string? name, ISet<string> taken)
        {
            if (KnownDriveSlugs.TryGetValue(driveId, out var known))
            {
                if (taken.Contains(known))
                {
                    throw new OdinSystemException(
                        $"System drive slug '{known}' is already taken; cannot assign it to {driveId}");
                }

                return known;
            }

            // Derived, in order of preference: the name, then the name with a numeric suffix, then the
            // drive id. The last is unreadable but always available, and beats refusing to migrate.
            var baseSlug = Slugify(name);

            if (baseSlug != null && !taken.Contains(baseSlug))
            {
                return baseSlug;
            }

            if (baseSlug != null)
            {
                for (var suffix = 2; suffix <= 99; suffix++)
                {
                    var tail = "-" + suffix;
                    var trimmed = baseSlug[..Math.Min(baseSlug.Length, MaxLength - tail.Length)].TrimEnd('-');

                    if (trimmed.Length == 0)
                    {
                        break;
                    }

                    var candidate = trimmed + tail;
                    if (ValidSlug.IsMatch(candidate) && !taken.Contains(candidate))
                    {
                        return candidate;
                    }
                }
            }

            var fromId = driveId.ToString("N")[..MaxLength];
            if (!taken.Contains(fromId) && ValidSlug.IsMatch(fromId))
            {
                return fromId;
            }

            throw new OdinSystemException(
                $"Could not assign a unique slug to drive {driveId} (name '{name}')");
        }

        /// <summary>
        /// Derives a slug from a drive name, or null when nothing usable survives -- a name of only
        /// punctuation, for instance.
        /// </summary>
        public static string? Slugify(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var lowered = new StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
            {
                // Keep it to plain ASCII: the slug is a URL segment that must survive with no encoding.
                lowered.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');
            }

            var collapsed = NonSlugRun.Replace(lowered.ToString(), "-").Trim('-');

            if (collapsed.Length > MaxLength)
            {
                collapsed = collapsed[..MaxLength].TrimEnd('-');
            }

            return collapsed.Length > 0 && ValidSlug.IsMatch(collapsed) ? collapsed : null;
        }

        /// <summary>
        /// The one deliberate difference from the app generator: this defers to <see cref="OdinSlug"/>
        /// rather than keeping a third copy of the pattern, which also picks up the reserved-segment list.
        /// </summary>
        public static bool IsValid(string? slug)
        {
            return OdinSlug.IsValid(slug);
        }
    }
}
