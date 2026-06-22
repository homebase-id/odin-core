using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive.Query;

namespace Odin.Services.Contacts;

/// <summary>
/// Pulls profile data for an identity and merges it into that identity's contact (data only — status is
/// never stored). The data source follows the identity's <b>live</b> connection status:
/// <list type="bullet">
/// <item><b>Connected</b> → peer-query the identity's <c>ProfileDrive</c> over transit and merge
/// name/phone/email/location/birthday.</item>
/// <item><b>Not connected</b> → read the identity's public profile card (<c>pub/profile</c>,
/// anonymous), which yields the display name.</item>
/// </list>
///
/// <para>
/// <b>403 fallback:</b> a connected identity may not have granted us its <c>ProfileDrive</c>. When the
/// peer query returns 403 we fall back to the public profile, exactly as for a non-connected identity
/// (a 403 carrying an ICR-issue also revokes the local ICR via <c>PeerDriveQueryService</c>).
/// </para>
///
/// <para>
/// <b>Scope (this increment):</b> text fields only. The profile image (<c>prfl_pic</c>) fetch +
/// re-encrypt is a follow-up. All fetches are best-effort: on any error the contact is left unchanged.
/// </para>
/// </summary>
public class ContactEnrichmentService(
    ILogger<ContactEnrichmentService> logger,
    CircleNetworkService circleNetworkService,
    PeerDriveQueryService peerDriveQueryService,
    IDynamicHttpClientFactory httpClientFactory,
    ContactService contactService)
{
    /// <summary>
    /// Enrich the contact for <paramref name="odinId"/> from its profile, choosing the source by live
    /// connection status, and merge the result (data only). Best-effort: returns without mutating the
    /// contact on any peer/profile failure.
    /// </summary>
    public async Task EnrichAsync(OdinId odinId, IOdinContext odinContext)
    {
        bool connected;
        try
        {
            connected = await circleNetworkService.IsConnectedAsync(odinId, odinContext);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Enrich: could not resolve connection status for {odinId}; skipping", odinId);
            return;
        }

        logger.LogInformation("[contact-enrich] {odinId}: connected={connected}", odinId, connected);

        PeerContactContent content;
        ContactExtData extData = null;
        var source = "none";
        if (connected)
        {
            try
            {
                (content, extData) = await BuildFromPeerProfileAsync(odinId, odinContext);
                source = "peer-profile-drive";
            }
            catch (OdinSecurityException)
            {
                // Connected, but no access to their ProfileDrive (403) — or the ICR was just revoked
                // due to an ICR-issue. Either way, fall back to the public profile.
                logger.LogInformation("[contact-enrich] {odinId}: ProfileDrive query 403 → public-card fallback", odinId);
                content = await TryBuildFromPublicProfileAsync(odinId);
                source = "public-card (403 fallback)";
            }
            catch (Exception e)
            {
                // Peer offline / transit failure: leave the contact untouched (idempotent; a later
                // sync or reconcile converges it).
                logger.LogInformation(e, "Enrich: peer profile query failed for {odinId}; leaving contact unchanged", odinId);
                return;
            }
        }
        else
        {
            content = await TryBuildFromPublicProfileAsync(odinId);
            source = "public-card (not connected)";
        }

        var hasExtData = extData is { IsEmpty: false };

        logger.LogInformation(
            "[contact-enrich] {odinId}: path={source} → name={name} email={email} phone={phone} status={status} " +
            "nickname={nick} link={link} social={socialCount} extData={extData}",
            odinId, source,
            content?.Name?.DisplayName != null, content?.Email?.Email != null, content?.Phone?.Number != null,
            content?.Status != null, content?.Nickname != null, content?.Link != null,
            content?.Social?.Count ?? 0, hasExtData);

        if (content == null && !hasExtData)
        {
            logger.LogDebug("Enrich: no profile data found for {odinId}; nothing to merge", odinId);
            return;
        }

        // ext_data may be present even when no flat content fields were found; merge it onto a (possibly
        // bare) content carrying just the odinId.
        content ??= new PeerContactContent();
        content.OdinId = odinId.DomainName;
        await contactService.MergeAsync(content, ContactMergeSource.Enrichment, odinContext, extData);
    }

    private async Task<(PeerContactContent content, ContactExtData extData)> BuildFromPeerProfileAsync(
        OdinId odinId, IOdinContext odinContext)
    {
        var request = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = SystemDriveConstants.ProfileDrive,
                FileType = [ContactProfileAttributes.AttributeFileType],
                TagsMatchAtLeastOne = ContactProfileAttributes.QueryTypes
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 100,
                IncludeMetadataHeader = true // so AppData.Content (the attribute) comes back
            }
        };

        var result = await peerDriveQueryService.GetBatchAsync(odinId, request, FileSystemType.Standard, odinContext);

        logger.LogInformation("[contact-enrich] {odinId}: peer query asked for {askedCount} types, got {resultCount} file(s)",
            odinId, ContactProfileAttributes.QueryTypes.Length, result?.SearchResults?.Count() ?? 0);

        if (result?.SearchResults == null)
        {
            return (null, null);
        }

        // A peer can publish several attributes of the same type (e.g. a primary and a secondary
        // phone). Each carries an authored Priority (lower = preferred, matching odin-js); process in
        // ascending Priority order and keep the first non-empty value per field, so a higher-priority
        // attribute that happens to be empty never shadows a populated lower-priority one.
        var attributes = result.SearchResults
            .Select(h => (Tags: h.FileMetadata.AppData.Tags ?? new List<Guid>(), Block: TryGetAttributeBlock(h, odinContext)))
            .Where(x => x.Block?.Data != null)
            .OrderBy(x => x.Block.Priority ?? int.MaxValue)
            .ToList();

        // Per-attribute trace: tags (no-dash, as they appear in the data) + data keys. Lets us see whether
        // social/email attributes came back and whether their type tag is one we match on.
        foreach (var (tags, block) in attributes)
        {
            logger.LogInformation("[contact-enrich] {odinId}: attr tags=[{tags}] dataKeys=[{keys}] priority={priority}",
                odinId,
                string.Join(",", tags.Select(t => t.ToString("N"))),
                string.Join(",", block.Data?.Keys ?? Enumerable.Empty<string>()),
                block.Priority);
        }

        var content = new PeerContactContent();
        ContactExtData extData = null;
        var found = false;

        foreach (var (tags, block) in attributes)
        {
            var data = block.Data;

            // ext_data attributes (Experience/Bio, …) are stored verbatim, keyed by type id — the server
            // never parses their data. First (highest-priority) wins per type, mirroring the flat fields.
            var extType = ContactProfileAttributes.ExtDataTypes.FirstOrDefault(tags.Contains);
            if (extType != Guid.Empty)
            {
                if (data is { Count: > 0 })
                {
                    extData ??= new ContactExtData { Attributes = new Dictionary<string, JsonElement>() };
                    extData.Attributes.TryAdd(extType.ToString("N"), JsonSerializer.SerializeToElement(data));
                }

                continue;
            }

            // Social/game handles: keyed verbatim by the attribute's type id (the chosen GUID keying),
            // value is the handle (a social attribute's data is a single { "<network>": "<handle>" } pair,
            // e.g. data["twitter"] = "@frodo"). Different networks accumulate; the first (highest-priority)
            // attribute of a given type wins, mirroring the flat fields.
            var socialType = ContactProfileAttributes.SocialTypes.FirstOrDefault(tags.Contains);
            if (socialType != Guid.Empty)
            {
                var handle = FirstValue(data);
                if (handle != null)
                {
                    content.Social ??= new Dictionary<string, string>();
                    // Key by the type id in the data's no-dash form (toGuidId / ToString("N")), matching
                    // ext_data and what clients compare against.
                    if (content.Social.TryAdd(socialType.ToString("N"), handle))
                    {
                        found = true;
                    }
                }

                continue;
            }

            // Link: a single personal link / website — keep the first (highest-priority) target URL.
            if (content.Link == null && tags.Contains(ContactProfileAttributes.Link))
            {
                var target = Str(data, ContactProfileAttributes.LinkTargetField);
                if (target != null)
                {
                    content.Link = target;
                    found = true;
                }

                continue;
            }

            if (content.Name == null && tags.Contains(ContactProfileAttributes.Name))
            {
                var name = new ContactName
                {
                    DisplayName = Str(data, ContactProfileAttributes.DisplayName),
                    GivenName = Str(data, ContactProfileAttributes.GivenName),
                    AdditionalName = Str(data, ContactProfileAttributes.AdditionalName),
                    Surname = Str(data, ContactProfileAttributes.Surname)
                };
                if (HasAnyValue(name))
                {
                    content.Name = name;
                    found = true;
                }
            }
            else if (content.Location == null && tags.Contains(ContactProfileAttributes.Address))
            {
                var location = new ContactLocation
                {
                    City = Str(data, ContactProfileAttributes.City),
                    Country = Str(data, ContactProfileAttributes.Country)
                };
                if (HasAnyValue(location))
                {
                    content.Location = location;
                    found = true;
                }
            }
            else if (content.Phone == null && tags.Contains(ContactProfileAttributes.PhoneNumber))
            {
                var number = Str(data, ContactProfileAttributes.PhoneNumberField);
                if (number != null)
                {
                    content.Phone = new ContactPhone { Number = number };
                    found = true;
                }
            }
            else if (content.Email == null && tags.Contains(ContactProfileAttributes.Email))
            {
                var email = Str(data, ContactProfileAttributes.EmailField);
                if (email != null)
                {
                    content.Email = new ContactEmail { Email = email };
                    found = true;
                }
            }
            else if (content.Birthday == null && tags.Contains(ContactProfileAttributes.Birthday))
            {
                var date = Str(data, ContactProfileAttributes.BirthdayDate);
                if (date != null)
                {
                    content.Birthday = new ContactBirthday { Date = date };
                    found = true;
                }
            }
            else if (content.ShortBio == null && tags.Contains(ContactProfileAttributes.ShortBioType))
            {
                // The "Short bio" attribute's data.short_bio is a plain string (≤160 chars) — distinct
                // from the rich-text short_bio in the "Bio" attribute, which is handled by ext_data above.
                var shortBio = Str(data, ContactProfileAttributes.ShortBioField);
                if (shortBio != null)
                {
                    content.ShortBio = shortBio;
                    found = true;
                }
            }
            else if (content.Status == null && tags.Contains(ContactProfileAttributes.Status))
            {
                var status = Str(data, ContactProfileAttributes.StatusField);
                if (status != null)
                {
                    content.Status = status;
                    found = true;
                }
            }
            else if (content.Nickname == null && tags.Contains(ContactProfileAttributes.Nickname))
            {
                var nickname = Str(data, ContactProfileAttributes.NicknameField);
                if (nickname != null)
                {
                    content.Nickname = nickname;
                    found = true;
                }
            }
        }

        return (found ? content : null, extData);
    }

    private static bool HasAnyValue(ContactName name)
    {
        return name.DisplayName != null || name.GivenName != null || name.AdditionalName != null || name.Surname != null;
    }

    private static bool HasAnyValue(ContactLocation location)
    {
        return location.City != null || location.Country != null;
    }

    /// <summary>
    /// Best-effort anonymous fetch of the identity's public profile card (<c>https://{odinId}/pub/profile</c>),
    /// used when the peer's ProfileDrive isn't reachable (not connected, or a 403). Pulls the clean scalar
    /// fields the card carries: name (display/given/surname), status, and the short bio.
    /// <para>
    /// <b>Not</b> reconstructed here: social handles and links. The card stores those lossily — full URLs
    /// keyed by network short-code — whereas <see cref="ContactContent.Social"/> is keyed by attribute type
    /// id with the raw handle. Those enrich only via the connected peer-ProfileDrive path. Image is also a
    /// follow-up. Returns null when the card has nothing usable (or on any failure).
    /// </para>
    /// </summary>
    private async Task<PeerContactContent> TryBuildFromPublicProfileAsync(OdinId odinId)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(odinId.DomainName);
            client.Timeout = TimeSpan.FromSeconds(10);

            using var response = await client.GetAsync($"https://{odinId.DomainName}/pub/profile");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Enrich: public profile for {odinId} returned {status}", odinId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var card = OdinSystemSerializer.Deserialize<PublicProfileCard>(json);
            if (card == null)
            {
                return null;
            }

            var content = new PeerContactContent();

            var name = new ContactName
            {
                DisplayName = Blank(card.Name),
                GivenName = Blank(card.GivenName),
                Surname = Blank(card.FamilyName)
            };
            if (HasAnyValue(name))
            {
                content.Name = name;
            }

            content.Status = Blank(card.Status);
            content.ShortBio = Blank(card.BioSummary);

            // Nothing usable on the card → leave the contact untouched.
            if (content.Name == null && content.Status == null && content.ShortBio == null)
            {
                return null;
            }

            return content;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Enrich: failed to fetch public profile for {odinId}; leaving contact unchanged", odinId);
            return null;
        }
    }

    /// <summary>
    /// Decrypts and parses a peer profile attribute header into the local <see cref="ProfileAttribute"/>
    /// (peer query re-encrypts the key header to our shared secret; unencrypted attributes come back as
    /// plaintext). Returns null when the content is missing or unparseable.
    /// </summary>
    private ProfileAttribute TryGetAttributeBlock(SharedSecretEncryptedFileHeader header, IOdinContext odinContext)
    {
        var raw = header.FileMetadata.AppData.Content;
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        string json;
        if (header.FileMetadata.IsEncrypted)
        {
            var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
            var keyHeader = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            json = keyHeader.Decrypt(Convert.FromBase64String(raw)).ToStringFromUtf8Bytes();
        }
        else
        {
            json = raw;
        }

        try
        {
            return OdinSystemSerializer.Deserialize<ProfileAttribute>(json);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Enrich: could not deserialize a profile attribute; skipping it");
            return null;
        }
    }

    private static string Str(Dictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        var s = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>Returns null for a null/whitespace string, the value otherwise (the field-merge convention).</summary>
    private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// The first non-empty value in a social attribute's data object (its single <c>{ network: handle }</c>
    /// pair, mirroring odin-js <c>Object.values(data)[0]</c>). Returns null when there is no usable handle.
    /// </summary>
    private static string FirstValue(Dictionary<string, object> data)
    {
        if (data == null)
        {
            return null;
        }

        foreach (var value in data.Values)
        {
            var s = value == null ? null : Convert.ToString(value);
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// The subset of a peer profile attribute this service consumes. Deliberately owned by the Contacts
    /// namespace — not shared with the SSR profile block — so the enrichment wire-mapping cannot drift
    /// when unrelated profile code changes. Extra fields on the wire are ignored.
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class ProfileAttribute
    {
        /// <summary>Authored rank; lower is preferred. Null sorts last.</summary>
        [JsonPropertyName("priority")]
        public int? Priority { get; init; }

        [JsonPropertyName("data")]
        public Dictionary<string, object> Data { get; init; }
    }

    /// <summary>
    /// The public profile card served at <c>pub/profile</c> (odin-js <c>ProfileCard</c>). Only the clean
    /// scalar fields are mapped; social/link arrays on the card are intentionally not consumed (see
    /// <see cref="TryBuildFromPublicProfileAsync"/>). Extra fields on the wire are ignored.
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class PublicProfileCard
    {
        [JsonPropertyName("name")]
        public string Name { get; init; }

        [JsonPropertyName("givenName")]
        public string GivenName { get; init; }

        [JsonPropertyName("familyName")]
        public string FamilyName { get; init; }

        [JsonPropertyName("status")]
        public string Status { get; init; }

        /// <summary>The short bio / tagline (odin-js card <c>bioSummary</c>), mapped to Content.ShortBio.</summary>
        [JsonPropertyName("bioSummary")]
        public string BioSummary { get; init; }
    }
}
