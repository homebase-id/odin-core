using System;
using System.Collections.Generic;
using System.Linq;
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

        ContactContent content;
        if (connected)
        {
            try
            {
                content = await BuildFromPeerProfileAsync(odinId, odinContext);
            }
            catch (OdinSecurityException)
            {
                // Connected, but no access to their ProfileDrive (403) — or the ICR was just revoked
                // due to an ICR-issue. Either way, fall back to the public profile.
                logger.LogDebug("Enrich: 403 querying {odinId} ProfileDrive; falling back to public profile", odinId);
                content = await TryBuildFromPublicProfileAsync(odinId);
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

        if (content == null)
        {
            logger.LogDebug("Enrich: no profile data found for {odinId}; nothing to merge", odinId);
            return;
        }

        content.OdinId = odinId.DomainName;
        await contactService.MergeAsync(content, ContactMergeSource.Enrichment, odinContext);
    }

    private async Task<ContactContent> BuildFromPeerProfileAsync(OdinId odinId, IOdinContext odinContext)
    {
        var request = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = SystemDriveConstants.ProfileDrive,
                FileType = [ContactProfileAttributes.AttributeFileType],
                TagsMatchAtLeastOne = ContactProfileAttributes.TextTypes
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
            return null;
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

        var content = new ContactContent();
        var found = false;

        foreach (var (tags, block) in attributes)
        {
            var data = block.Data;

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
        }

        return found ? content : null;
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
    /// yielding the display name. Returns null on any failure (offline, no card, non-OK). Image is not
    /// fetched here (follow-up).
    /// </summary>
    private async Task<ContactContent> TryBuildFromPublicProfileAsync(OdinId odinId)
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
            if (string.IsNullOrWhiteSpace(card?.Name))
            {
                return null;
            }

            return new ContactContent { Name = new ContactName { DisplayName = card.Name } };
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

    /// <summary>The public profile card served at <c>pub/profile</c> (odin-js <c>ProfileCard</c>).</summary>
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class PublicProfileCard
    {
        [JsonPropertyName("name")]
        public string Name { get; init; }
    }
}
