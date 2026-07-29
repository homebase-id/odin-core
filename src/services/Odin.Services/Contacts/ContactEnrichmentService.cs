using System;
using System.Collections.Generic;
using System.IO;
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
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
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

        PeerContactContent content;
        ContactExtData extData = null;

        // The peer's own photo. Null means "determined nothing this run" (leave the stored one alone);
        // PeerContactImage.None means "no photo we can see" and drops it. Losing visibility — not
        // connected, or a 403 — is a removal: we must not keep showing a gated photo we can no longer
        // read. It never touches the user's own prfl_pic choice for this contact.
        PeerContactImage peerImage = PeerContactImage.None;

        if (connected)
        {
            try
            {
                (content, extData, peerImage) = await BuildFromPeerProfileAsync(odinId, odinContext);
            }
            catch (OdinSecurityException)
            {
                // Connected, but no access to their ProfileDrive (403) — or the ICR was just revoked
                // due to an ICR-issue. Either way, fall back to the public profile.
                logger.LogDebug("Enrich: 403 querying {odinId} ProfileDrive; falling back to public profile", odinId);
                content = await TryBuildFromPublicProfileAsync(odinId);
                peerImage = PeerContactImage.None;
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
        }

        var hasExtData = extData is { IsEmpty: false };
        var hasImage = peerImage is { IsRemoval: false };
        if (content == null && !hasExtData && !hasImage)
        {
            // Nothing usable at all — don't merge, so we never conjure an empty contact for an identity
            // that published nothing. Consequence: a peer who clears their *entire* profile keeps any
            // peer photo we already hold; one who clears only the photo (still has a name) drops it on
            // this same run.
            logger.LogDebug("Enrich: no profile data found for {odinId}; nothing to merge", odinId);
            return;
        }

        // ext_data / the photo may be present even when no flat content fields were found; merge onto a
        // (possibly bare) content carrying just the odinId.
        content ??= new PeerContactContent();
        content.OdinId = odinId.DomainName;
        await contactService.MergeAsync(content, ContactMergeSource.Enrichment, odinContext, extData, peerImage);
    }

    private async Task<(PeerContactContent content, ContactExtData extData, PeerContactImage peerImage)> BuildFromPeerProfileAsync(
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
        if (result?.SearchResults == null)
        {
            return (null, null, null);
        }

        // A peer can publish several attributes of the same type (e.g. a primary and a secondary
        // phone). Each carries an authored Priority (lower = preferred, matching odin-js); process in
        // ascending Priority order and keep the first non-empty value per field, so a higher-priority
        // attribute that happens to be empty never shadows a populated lower-priority one.
        // The header rides along so the Photo attribute can be resolved to its payload afterwards.
        var attributes = result.SearchResults
            .Select(h => (Tags: h.FileMetadata.AppData.Tags ?? new List<Guid>(), Block: TryGetAttributeBlock(h, odinContext),
                Header: h))
            .Where(x => x.Block?.Data != null)
            .OrderBy(x => x.Block.Priority ?? int.MaxValue)
            .ToList();

        // The Photo attribute's image is a payload, not a content field — fetch it separately. The
        // highest-priority photo the peer lets us see wins; a peer publishing both a public avatar and a
        // richer connected-only one therefore gives us whichever their ACLs admit us to.
        var peerImage = await TryFetchPeerImageAsync(odinId,
            attributes.Where(x => x.Tags.Contains(ContactProfileAttributes.Photo))
                .Select(x => (x.Block, x.Header))
                .FirstOrDefault(),
            odinContext);

        var content = new PeerContactContent();
        ContactExtData extData = null;
        var found = false;

        foreach (var (tags, block, _) in attributes)
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
                    Label = Str(data, ContactProfileAttributes.Label),
                    AddressLine1 = Str(data, ContactProfileAttributes.AddressLine1),
                    AddressLine2 = Str(data, ContactProfileAttributes.AddressLine2),
                    Postcode = Str(data, ContactProfileAttributes.Postcode),
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
                    content.Phone = new ContactPhone { Number = number, Label = Str(data, ContactProfileAttributes.Label) };
                    found = true;
                }
            }
            else if (content.Email == null && tags.Contains(ContactProfileAttributes.Email))
            {
                var email = Str(data, ContactProfileAttributes.EmailField);
                if (email != null)
                {
                    content.Email = new ContactEmail { Email = email, Label = Str(data, ContactProfileAttributes.Label) };
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

        return (found ? content : null, extData, peerImage);
    }

    /// <summary>
    /// Resolves the peer's Photo attribute to its actual image bytes: the attribute header only carries a
    /// pointer (<c>data.profileImageKey</c>) at a payload on the same file, so the payload — and every
    /// thumbnail the peer published for it — is fetched over transit and decrypted with our shared
    /// secret. Returns <see cref="PeerContactImage.None"/> when the peer publishes no photo we can see
    /// (so a stale one gets cleared), and <c>null</c> when a fetch failed midway and the stored photo
    /// should simply be left alone.
    /// </summary>
    private async Task<PeerContactImage> TryFetchPeerImageAsync(OdinId odinId,
        (ProfileAttribute Block, SharedSecretEncryptedFileHeader Header) photo, IOdinContext odinContext)
    {
        if (photo.Block == null || photo.Header == null)
        {
            return PeerContactImage.None;
        }

        var payloadKey = Str(photo.Block.Data, ContactProfileAttributes.ProfileImageKeyField);
        if (payloadKey == null)
        {
            logger.LogDebug("Enrich: {odinId} photo attribute has no {field}; treating as no photo",
                odinId, ContactProfileAttributes.ProfileImageKeyField);
            return PeerContactImage.None;
        }

        var descriptor = photo.Header.FileMetadata.Payloads?.FirstOrDefault(p => p.KeyEquals(payloadKey));
        if (descriptor == null)
        {
            logger.LogDebug("Enrich: {odinId} photo attribute points at payload {key} which isn't on the file",
                odinId, payloadKey);
            return PeerContactImage.None;
        }

        if (descriptor.BytesWritten > ContactService.MaxPeerImageBytes)
        {
            logger.LogDebug("Enrich: {odinId} photo payload is {size} bytes, over the {cap} cap; skipping",
                odinId, descriptor.BytesWritten, ContactService.MaxPeerImageBytes);
            return PeerContactImage.None;
        }

        var file = new ExternalFileIdentifier
        {
            TargetDrive = SystemDriveConstants.ProfileDrive,
            FileId = photo.Header.FileId
        };

        try
        {
            var (keyHeader, isEncrypted, payloadStream) = await peerDriveQueryService.GetPayloadStreamAsync(
                odinId, file, payloadKey, null, FileSystemType.Standard, odinContext);

            if (payloadStream == null)
            {
                return PeerContactImage.None;
            }

            byte[] image;
            using (payloadStream)
            {
                image = await DecryptPeerPayloadAsync(payloadStream.Stream, keyHeader, isEncrypted, odinContext);
            }

            if (image is not { Length: > 0 })
            {
                return PeerContactImage.None;
            }

            if (image.Length > ContactService.MaxPeerImageBytes)
            {
                // The descriptor's BytesWritten is the *ciphertext* length; re-check the plaintext.
                logger.LogDebug("Enrich: {odinId} decrypted photo is over the {cap} byte cap; skipping",
                    odinId, ContactService.MaxPeerImageBytes);
                return PeerContactImage.None;
            }

            var thumbnails = new List<PeerContactImageThumbnail>();
            foreach (var thumb in descriptor.Thumbnails ?? [])
            {
                var bytes = await TryFetchPeerThumbnailAsync(odinId, file, payloadKey, thumb, odinContext);
                if (bytes is { Length: > 0 })
                {
                    thumbnails.Add(new PeerContactImageThumbnail
                    {
                        PixelWidth = thumb.PixelWidth,
                        PixelHeight = thumb.PixelHeight,
                        ContentType = thumb.ContentType,
                        Content = bytes
                    });
                }
            }

            return new PeerContactImage
            {
                Content = image,
                ContentType = descriptor.ContentType,
                Thumbnails = thumbnails
            };
        }
        catch (OdinSecurityException)
        {
            // The attribute header was readable but the payload isn't — treat as no photo rather than
            // failing the whole enrichment.
            logger.LogDebug("Enrich: 403 fetching {odinId} photo payload; treating as no photo", odinId);
            return PeerContactImage.None;
        }
        catch (Exception e)
        {
            // Transit hiccup partway through: leave whatever photo we already hold alone.
            logger.LogInformation(e, "Enrich: could not fetch photo for {odinId}; leaving the stored photo unchanged", odinId);
            return null;
        }
    }

    /// <summary>
    /// Best-effort fetch of one published thumbnail rendition. A rendition that fails is skipped — the
    /// full-size image alone is still a usable result.
    /// </summary>
    private async Task<byte[]> TryFetchPeerThumbnailAsync(OdinId odinId, ExternalFileIdentifier file, string payloadKey,
        ThumbnailDescriptor thumb, IOdinContext odinContext)
    {
        try
        {
            var (keyHeader, isEncrypted, _, _, stream) = await peerDriveQueryService.GetThumbnailAsync(
                odinId, file, thumb.PixelWidth, thumb.PixelHeight, payloadKey, FileSystemType.Standard, odinContext);

            if (stream == null)
            {
                return null;
            }

            await using (stream)
            {
                return await DecryptPeerPayloadAsync(stream, keyHeader, isEncrypted, odinContext);
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Enrich: skipping {w}x{h} thumbnail for {odinId}", thumb.PixelWidth, thumb.PixelHeight, odinId);
            return null;
        }
    }

    /// <summary>
    /// Reads a peer payload/thumbnail stream and decrypts it. The peer query re-encrypts the payload's
    /// key header to our shared secret and bakes the payload's own IV into it, so this is a straight
    /// decrypt; an unencrypted (anonymous-tier) payload comes back as plaintext.
    /// </summary>
    private static async Task<byte[]> DecryptPeerPayloadAsync(Stream stream, EncryptedKeyHeader keyHeader,
        bool isEncrypted, IOdinContext odinContext)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        if (!isEncrypted || bytes.Length == 0)
        {
            return bytes;
        }

        var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
        return keyHeader.DecryptAesToKeyHeader(ref sharedSecret).Decrypt(bytes);
    }

    private static bool HasAnyValue(ContactName name)
    {
        return name.DisplayName != null || name.GivenName != null || name.AdditionalName != null || name.Surname != null;
    }

    private static bool HasAnyValue(ContactLocation location)
    {
        return location.Label != null || location.AddressLine1 != null || location.AddressLine2 != null
               || location.Postcode != null || location.City != null || location.Country != null;
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
