#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Odin.Core.Exceptions;
using Odin.Services.Apps;

namespace Odin.Services.Authorization.Apps
{
    /// <summary>
    /// Assigns an <c>AppSlug</c> to app registrations that predate the column.
    /// </summary>
    /// <remarks>
    /// A slug is a URL path segment and a wire address that other identities resolve against, so it is
    /// immutable once written and the column is <c>UNIQUE(identityId, AppSlug)</c>.  Normal registration
    /// validates a caller-supplied slug and rejects a bad one; there is nobody to reject here, so this
    /// derives one instead - the only place in the system that coins a slug rather than being handed one.
    /// <para>
    /// The known system apps get the obvious name.  Everything else is derived from the registration's
    /// display name, which is the only human-meaningful thing on the record.  Names collide and can
    /// slugify to nothing, so the whole set is resolved and checked up front by
    /// <see cref="GenerateAll"/>: nothing is written until every app has a valid, unique slug.
    /// </para>
    /// </remarks>
    public static class AppSlugGenerator
    {
        public const int MaxLength = 12;

        private static readonly Regex ValidSlug = new("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);
        private static readonly Regex NonSlugRun = new("[^a-z0-9]+", RegexOptions.Compiled);

        /// <summary>
        /// Slugs for the apps that ship with every identity.  These are the addresses the drive-addressing
        /// work assumes (<c>/apps/chat/drives/messages</c>), so they are assigned rather than derived.
        /// </summary>
        private static readonly Dictionary<Guid, string> KnownAppSlugs = new()
        {
            [SystemAppConstants.OwnerAppId] = "owner",
            [SystemAppConstants.ChatAppId] = "chat",
            [SystemAppConstants.FeedAppId] = "feed",
            [SystemAppConstants.PhotoAppId] = "photo",
            [SystemAppConstants.MailAppId] = "mail"
        };

        /// <summary>
        /// Resolves a slug for every app in one pass, so a collision is found before anything is written.
        /// </summary>
        /// <exception cref="OdinSystemException">
        /// If a unique, valid slug cannot be produced for some app.  Failing here leaves the source data
        /// untouched, which is the point of doing this up front.
        /// </exception>
        public static Dictionary<Guid, string> GenerateAll(IEnumerable<(Guid AppId, string? Name)> apps)
        {
            var result = new Dictionary<Guid, string>();
            var taken = new HashSet<string>(StringComparer.Ordinal);

            // Known apps first: their slugs are fixed, so a derived slug must yield to them rather than
            // the other way round.
            var ordered = apps
                .OrderByDescending(a => KnownAppSlugs.ContainsKey(a.AppId))
                .ThenBy(a => a.AppId)
                .ToList();

            foreach (var (appId, name) in ordered)
            {
                if (result.ContainsKey(appId))
                {
                    throw new OdinSystemException($"Duplicate app id {appId} while assigning slugs");
                }

                var slug = Resolve(appId, name, taken);

                result[appId] = slug;
                taken.Add(slug);
            }

            return result;
        }

        /// <summary>
        /// Picks a slug for one app, avoiding everything in <paramref name="taken"/>.
        /// </summary>
        /// <remarks>
        /// Pass the slugs actually stored, not re-derived ones: an app whose stored slug is
        /// <c>acme-2</c> must still be treated as holding <c>acme-2</c>, not whatever its name would
        /// slugify to today.
        /// </remarks>
        public static string Generate(Guid appId, string? name, ISet<string> taken)
        {
            return Resolve(appId, name, taken);
        }

        private static string Resolve(Guid appId, string? name, ISet<string> taken)
        {
            if (KnownAppSlugs.TryGetValue(appId, out var known))
            {
                if (taken.Contains(known))
                {
                    throw new OdinSystemException(
                        $"System app slug '{known}' is already taken; cannot assign it to {appId}");
                }

                return known;
            }

            // Derived, in order of preference: the name, then the name with a numeric suffix, then the
            // app id. The last is unreadable but always available, and beats refusing to migrate.
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

            var fromId = appId.ToString("N")[..MaxLength];
            if (!taken.Contains(fromId) && ValidSlug.IsMatch(fromId))
            {
                return fromId;
            }

            throw new OdinSystemException(
                $"Could not assign a unique slug to app {appId} (name '{name}')");
        }

        /// <summary>
        /// Derives a slug from a display name, or null when nothing usable survives -- a name of only
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

        public static bool IsValid(string? slug)
        {
            return !string.IsNullOrEmpty(slug) && slug.Length <= MaxLength && ValidSlug.IsMatch(slug);
        }
    }
}
