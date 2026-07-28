using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.PublicPage.PersonMetadata;
using Odin.Services.Mediator;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.Profile;

/// <summary>
/// Republishes the public, static-file artifacts derived from profile attributes -- <c>sitedata.json</c>,
/// <c>public_image.json</c> (served at <c>/pub/image</c>) and <c>public_profile.json</c> (served at
/// <c>/pub/profile</c>, consumed server-side by <see cref="Odin.Services.PublicPage.Profile.HomebaseProfileContentService"/>)
/// -- whenever a profile attribute changes. This is the server-side counterpart of odin-js's
/// <c>publishProfile</c>/<c>publishProfileImage</c>/<c>publishProfileCard</c>
/// (<c>packages/libs/js-lib/src/public/file/*</c>), which only fire from the owner-app's UI mutation hooks.
///
/// <para>
/// Reacts generically to <see cref="DriveFileAddedNotification"/>/<see cref="DriveFileChangedNotification"/>/
/// <see cref="DriveFileDeletedNotification"/> for the ProfileDrive rather than requiring each writer to call
/// <see cref="PublishAsync"/> by hand -- so this fires for <em>any</em> profile-attribute write, including
/// ones from <see cref="ProfileAttributeService"/>, the owner-app's own direct uploads, or any future writer,
/// without each of them needing to remember to trigger a republish.
/// </para>
///
/// <para>
/// Runs inline, synchronously, right after an attribute write commits -- a single user does not edit
/// profile attributes often enough for the extra query/read latency to matter. Every step is best-effort:
/// a failure here is logged and swallowed rather than turning a successful attribute write into a 500.
/// </para>
/// </summary>
public class ProfilePublishService(
    StaticFileContentService staticFileContentService,
    StandardFileSystem fileSystem,
    IDriveManager driveManager,
    IMediator mediator,
    ILogger<ProfilePublishService> logger)
    : INotificationHandler<DriveFileAddedNotification>,
        INotificationHandler<DriveFileChangedNotification>,
        INotificationHandler<DriveFileDeletedNotification>
{
    private const int ChannelDefinitionFileType = 103;
    private const string DefaultPayloadKey = "dflt_key";

    private static readonly TargetDrive ProfileDrive = SystemDriveConstants.ProfileDrive;
    private static readonly TargetDrive HomePageConfigDrive = SystemDriveConstants.HomePageConfigDrive;

    // odin-js HomePageAttributes.Theme = toGuidId("theme_attribute"); not part of BuiltInProfileAttributes
    // since Theme attributes live on the HomePageConfigDrive, not the ProfileDrive, and are out of scope
    // for ProfileAttributeService itself -- only needed here to query the "theme" sitedata.json section.
    private static readonly Guid ThemeAttributeType = new("8f7eb1c3-2fc7-2c0a-bf0c-ee09be588f26");

    private static readonly SectionResultOptions BaseResultOptions = new()
    {
        IncludeHeaderContent = true,
        PayloadKeys = [DefaultPayloadKey],
        ExcludePreviewThumbnail = false
    };

    private static readonly IReadOnlyList<ProfileAttributeType> SocialAndGameTypes = BuiltInProfileAttributes.All
        .Where(t => t.Category is ProfileAttributeCategory.Social or ProfileAttributeCategory.Game)
        .ToList();

    // odin-js ProfileCardManager.ts ProfileCardAttributeTypes -- deliberately NOT the same set as the
    // sitedata.json trigger set below (this one adds Email, and omits Experience/long-bio).
    private static readonly HashSet<Guid> ProfileCardTriggerTypes =
    [
        BuiltInProfileAttributes.Name,
        BuiltInProfileAttributes.Email,
        BuiltInProfileAttributes.Link,
        BuiltInProfileAttributes.Photo,
        BuiltInProfileAttributes.Bio,
        BuiltInProfileAttributes.BioSummary,
        BuiltInProfileAttributes.Status,
        .. SocialAndGameTypes.Select(t => t.Type)
    ];

    private static readonly Lazy<List<QueryParamSection>> FixedSiteDataSections = new(BuildFixedSiteDataSections);

    private static readonly Lazy<HashSet<Guid>> SiteDataTriggerTypes = new(() => FixedSiteDataSections.Value
        .SelectMany(s => s.QueryParams.TagsMatchAtLeastOne ?? [])
        .ToHashSet());

    /// <param name="attributeType">
    /// The type of the attribute that was just created/updated/deleted, or <c>null</c> to republish
    /// everything (mirrors odin-js's <c>publishStaticFiles(undefined)</c> full-refresh case).
    /// </param>
    public async Task PublishAsync(Guid? attributeType, IOdinContext odinContext)
    {
        var publishContext = BuildSystemContext(odinContext.Tenant);

        if (attributeType == null || SiteDataTriggerTypes.Value.Contains(attributeType.Value))
        {
            await TryRunAsync(() => RepublishSiteDataAsync(publishContext), "sitedata.json");
        }

        if (attributeType == null || attributeType == BuiltInProfileAttributes.Photo || attributeType == BuiltInProfileAttributes.Name)
        {
            await TryRunAsync(() => RepublishProfileImageAsync(publishContext), StaticFileConstants.ProfileImageFileName);
        }

        if (attributeType == null || ProfileCardTriggerTypes.Contains(attributeType.Value))
        {
            await TryRunAsync(() => RepublishProfileCardAsync(publishContext), StaticFileConstants.PublicProfileCardFileName);
        }
    }

    public Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken) =>
        HandleDriveEventAsync(notification.File.DriveId, notification.ServerFileHeader, notification.OdinContext);

    public Task Handle(DriveFileChangedNotification notification, CancellationToken cancellationToken) =>
        HandleDriveEventAsync(notification.File.DriveId, notification.ServerFileHeader, notification.OdinContext);

    public Task Handle(DriveFileDeletedNotification notification, CancellationToken cancellationToken) =>
        // The live header's content/tags are already cleared by the delete; PreviousServerFileHeader still
        // carries the attribute's type tag, which is all PublishAsync needs.
        HandleDriveEventAsync(notification.File.DriveId, notification.PreviousServerFileHeader, notification.OdinContext);

    /// <summary>
    /// Common entry point for all three drive-write notifications: only profile-attribute files on the
    /// ProfileDrive are in scope (odin-js's own direct uploads and any other file on this drive are ignored).
    /// The attribute type -- needed to decide which artifacts a change can possibly affect -- comes from the
    /// header's own tags (<c>[type, sectionId, profileId, id]</c>, see BuildHeaderAsync/BuildPhotoMetadata)
    /// rather than a caller-supplied parameter, since any writer can reach this path.
    /// </summary>
    private async Task HandleDriveEventAsync(Guid driveId, ServerFileHeader header, IOdinContext odinContext)
    {
        if (driveId != ProfileDrive.Alias || header?.FileMetadata?.AppData?.FileType != ProfileAttributeService.AttributeFileType)
        {
            return;
        }

        var tags = header.FileMetadata.AppData.Tags;
        Guid? attributeType = tags is { Count: > 0 } ? tags[0] : null;

        await PublishAsync(attributeType, odinContext);
    }

    private async Task TryRunAsync(Func<Task> action, string artifact)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to republish {artifact} after a profile attribute change", artifact);
        }
    }

    /// <summary>
    /// A system-level context, bypassing per-drive grants and the PublishStaticContent permission check
    /// (<see cref="PermissionContext.HasDrivePermission"/>/<see cref="PermissionContext.HasPermission"/> both
    /// short-circuit to true when isSystem is set) -- same isSystem-bypass pattern already used by
    /// <see cref="Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.PeerOutboxProcessorBackgroundService"/>
    /// for its own background tenant-wide work. Needed because the sections below span the ProfileDrive,
    /// the HomePageConfigDrive and however many channel drives are discovered at runtime -- more than the
    /// original caller's ManageProfile grant covers. Safe because everything read and republished here is
    /// filtered down to Anonymous-ACL, unencrypted content (the same filter
    /// <see cref="StaticFileContentService.PublishAsync"/> already applies) -- nothing this exposes wasn't
    /// already meant to be public.
    ///
    /// <para>
    /// Deliberately uses <see cref="SecurityGroupType.Owner"/>, not <see cref="SecurityGroupType.System"/>,
    /// for <see cref="CallerContext.SecurityLevel"/>: the tag-query path (<c>DriveQuery.GetBatchCoreAsync</c>)
    /// builds a row-level SQL filter <c>requiredSecurityGroup BETWEEN 0 AND (int)Caller.SecurityLevel</c>.
    /// <see cref="SecurityGroupType"/>'s numeric values are Anonymous=111 ... Owner=999 but System=1 --
    /// below every real ACL level -- so a System-level caller's queries silently match zero rows despite
    /// the isSystem permission bypass (that bypass only covers the drive-grant gate *before* the query
    /// runs, not this row filter). Owner's numeric value happens to be the range's max, so it covers every
    /// ACL level; <see cref="DriveAclAuthorizationService.CallerHasPermission"/>'s post-query check also
    /// short-circuits true for <c>caller.IsOwner</c>, same as it does for System.
    /// </para>
    ///
    /// <para>
    /// isSystem does NOT bypass <see cref="PermissionContext.GetTargetDrive"/> -- <see cref="DriveFileUtility.CreateClientFileHeader"/>
    /// calls it to populate the result's <c>TargetDrive</c> field, and it always looks the drive up in an
    /// actual <see cref="DriveGrant"/>, throwing <see cref="Odin.Core.Exceptions.OdinSecurityException"/> if
    /// none is registered. So every drive queried through this context needs a (harmless, since isSystem
    /// already bypasses the permission check itself) grant added via <see cref="GrantDriveAccess"/> --
    /// ProfileDrive and HomePageConfigDrive upfront here; channel drives as they're discovered in
    /// <see cref="BuildChannelSectionsAsync"/>.
    /// </para>
    /// </summary>
    private static IOdinContext BuildSystemContext(OdinId tenant)
    {
        var context = new OdinContext
        {
            Tenant = tenant,
            AuthTokenCreated = null,
            Caller = new CallerContext(
                odinId: (OdinId)"system.domain",
                masterKey: null,
                securityLevel: SecurityGroupType.Owner,
                circleIds: null,
                tokenType: ClientTokenType.Other)
        };
        context.SetPermissionContext(new PermissionContext(null, null, isSystem: true));

        GrantDriveAccess(context, ProfileDrive);
        GrantDriveAccess(context, HomePageConfigDrive);

        return context;
    }

    /// <summary>
    /// Registers a (permission-check-irrelevant, since the isSystem PermissionContext already bypasses
    /// <see cref="PermissionContext.HasDrivePermission"/>) <see cref="DriveGrant"/> purely so
    /// <see cref="PermissionContext.GetTargetDrive"/> can resolve this drive's <see cref="TargetDrive"/> info.
    /// Same construction <see cref="Odin.Services.Base.OdinContextUpgrades.UpgradeToByPassAclCheck"/> uses.
    /// </summary>
    private static void GrantDriveAccess(IOdinContext context, TargetDrive drive)
    {
        var driveGrant = new DriveGrant
        {
            DriveId = drive.Alias,
            PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read },
            KeyStoreKeyEncryptedStorageKey = null
        };
        var group = new PermissionGroup(new PermissionSet([]), [driveGrant], null, null);
        context.PermissionsContext.PermissionGroups.TryAdd($"publish-{drive.Alias}", group);
    }

    //
    // sitedata.json
    //

    private async Task RepublishSiteDataAsync(IOdinContext publishContext)
    {
        var sections = new List<QueryParamSection>(FixedSiteDataSections.Value);
        sections.AddRange(await BuildChannelSectionsAsync(publishContext));

        var config = new StaticFileConfiguration { CrossOriginBehavior = CrossOriginBehavior.Default };
        await staticFileContentService.PublishAsync("sitedata.json", config, sections, publishContext);

        await mediator.Publish(new PublicProfileContentPublishedNotification
        {
            Artifact = PublicProfileArtifact.SiteData,
            OdinContext = publishContext
        });
    }

    // Mirrors odin-js FileBase.ts DEFAULT_SECTIONS exactly -- section names are read verbatim by the
    // public profile page; do not rename.
    private static List<QueryParamSection> BuildFixedSiteDataSections()
    {
        // No GroupId filter: the attribute-type tag alone already disambiguates (a built-in type maps 1:1
        // to its section), so requiring GroupId too only makes this fragile to writers that set Tags but
        // not GroupId, without adding any real safety.
        QueryParamSection Section(string name, TargetDrive drive, params Guid[] tags) => new()
        {
            Name = name,
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = drive,
                FileType = [ProfileAttributeService.AttributeFileType],
                TagsMatchAtLeastOne = tags
            },
            ResultOptions = BaseResultOptions
        };

        return
        [
            Section("socials", ProfileDrive, SocialAndGameTypes.Select(t => t.Type).ToArray()),
            Section("name", ProfileDrive, BuiltInProfileAttributes.Name),
            Section("photo", ProfileDrive, BuiltInProfileAttributes.Photo),
            Section("status", ProfileDrive, BuiltInProfileAttributes.Status),
            Section("long-bio", ProfileDrive, BuiltInProfileAttributes.Experience),
            Section("short-bio", ProfileDrive, BuiltInProfileAttributes.Bio),
            Section("short-bio-summary", ProfileDrive, BuiltInProfileAttributes.BioSummary),
            Section("link", ProfileDrive, BuiltInProfileAttributes.Link),
            Section("theme", HomePageConfigDrive, ThemeAttributeType)
        ];
    }

    // Appended to the fixed sections always, regardless of why we're republishing -- same as odin-js's
    // publishProfile, which merges DEFAULT_SECTIONS with the caller's public channels on every call.
    private async Task<List<QueryParamSection>> BuildChannelSectionsAsync(IOdinContext publishContext)
    {
        var sections = new List<QueryParamSection>();
        var channelDrives = await driveManager.GetDrivesAsync(SystemDriveConstants.ChannelDriveType, PageOptions.All, publishContext);

        foreach (var drive in channelDrives.Results)
        {
            var targetDrive = drive.TargetDriveInfo;
            GrantDriveAccess(publishContext, targetDrive);

            var qp = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                FileType = [ChannelDefinitionFileType]
            };
            var options = new QueryBatchResultOptions
            {
                MaxRecords = 1,
                IncludeHeaderContent = false,
                ExcludePreviewThumbnail = true,
                ExcludeServerMetaData = false
            };

            var batch = await fileSystem.Query.GetBatch(targetDrive.Alias, qp, options, publishContext);
            var definitionFile = batch.SearchResults.FirstOrDefault();
            if (definitionFile == null)
            {
                continue;
            }

            // Definition-file ACL check (is this channel itself public?), independent of the isSystem
            // drive-grant bypass above -- mirrors publishProfile's channel filter exactly.
            var acl = definitionFile.ServerMetadata?.AccessControlList?.RequiredSecurityGroup;
            if (acl != SecurityGroupType.Anonymous && acl != SecurityGroupType.Authenticated)
            {
                continue;
            }

            sections.Add(new QueryParamSection
            {
                Name = definitionFile.FileMetadata.AppData.UniqueId?.ToString() ?? targetDrive.Alias.Value.ToString("N"),
                QueryParams = new FileQueryParamsV1
                {
                    TargetDrive = targetDrive,
                    FileType = [ChannelDefinitionFileType]
                },
                ResultOptions = BaseResultOptions
            });
        }

        return sections;
    }

    //
    // public_image.json
    //

    private async Task RepublishProfileImageAsync(IOdinContext publishContext)
    {
        var photoAttribute = (await QueryAnonymousAttributesAsync([BuiltInProfileAttributes.Photo], publishContext,
            maxRecords: 1)).FirstOrDefault();
        if (photoAttribute == null)
        {
            await PublishFallbackProfileImageAsync(publishContext);
            return;
        }

        var content = DeserializeAttributeContent(photoAttribute.FileMetadata.AppData.Content);
        var payloadKey = GetDataString(content, ProfileAttributeFields.ProfileImageKey);
        if (string.IsNullOrWhiteSpace(payloadKey))
        {
            await PublishFallbackProfileImageAsync(publishContext);
            return;
        }

        var payloadDescriptor = photoAttribute.FileMetadata.Payloads?.FirstOrDefault(p => p.KeyEquals(payloadKey));
        if (payloadDescriptor == null)
        {
            await PublishFallbackProfileImageAsync(publishContext);
            return;
        }

        var file = new InternalDriveFileId(ProfileDrive.Alias, photoAttribute.FileId);

        byte[] imageBytes;
        string contentType;

        // Reuse the ~250px thumbnail odin-js already generates and uploads for every Photo attribute save
        // (profileInstructionThumbSizes) rather than decoding + resizing the full image server-side -- the
        // .NET server has no image-processing dependency today, and pulling one in just to reproduce a
        // 250x250 JPEG the client already computed isn't worth it. GetThumbnailPayloadStreamAsync already
        // finds the closest available size if an exact 250x250 match isn't there.
        var (thumbStream, thumbnail) = await fileSystem.Storage.GetThumbnailPayloadStreamAsync(
            file, 250, 250, payloadKey, payloadDescriptor.Uid, publishContext);

        if (thumbnail != null)
        {
            using (thumbStream)
            {
                imageBytes = thumbStream.ToByteArray();
            }

            contentType = thumbnail.ContentType;
        }
        else
        {
            using var payloadStream = await fileSystem.Storage.GetPayloadStreamAsync(file, payloadKey, null, publishContext);
            if (payloadStream == null)
            {
                await PublishFallbackProfileImageAsync(publishContext);
                return;
            }

            imageBytes = payloadStream.Stream.ToByteArray();
            contentType = payloadStream.ContentType;
        }

        await staticFileContentService.PublishProfileImageAsync(imageBytes.ToBase64(), contentType);

        await mediator.Publish(new PublicProfileContentPublishedNotification
        {
            Artifact = PublicProfileArtifact.ProfileImage,
            OdinContext = publishContext
        });
    }

    /// <summary>
    /// Called whenever <see cref="RepublishProfileImageAsync"/> has no usable Photo attribute to publish.
    /// Falls back, in order: a generated initials avatar (e.g. "JB") from an Anonymous-tier Name
    /// attribute if one exists; otherwise two letters derived from the domain itself (mirrors odin-js's
    /// own FallbackImg.tsx last-resort, which almost always has something to show); otherwise clears
    /// the artifact via <see cref="ClearProfileImageAsync"/>, the true last resort.
    /// </summary>
    private async Task PublishFallbackProfileImageAsync(IOdinContext publishContext)
    {
        var nameResults = await QueryAnonymousAttributesAsync([BuiltInProfileAttributes.Name], publishContext, maxRecords: 1);
        var nameContent = DeserializeAttributeContent(nameResults.FirstOrDefault()?.FileMetadata.AppData.Content);
        var givenName = GetDataString(nameContent, ProfileAttributeFields.GivenName);
        var surname = GetDataString(nameContent, ProfileAttributeFields.Surname);

        var tenant = publishContext.Tenant.ToString();
        var generated = InitialsAvatarGenerator.TryGenerate(givenName, surname, tenant, out var pngBase64) ||
                         InitialsAvatarGenerator.TryGenerateFromDomain(tenant, out pngBase64);

        if (generated)
        {
            await staticFileContentService.PublishProfileImageAsync(pngBase64!, "image/png");

            await mediator.Publish(new PublicProfileContentPublishedNotification
            {
                Artifact = PublicProfileArtifact.ProfileImage,
                OdinContext = publishContext
            });
            return;
        }

        await ClearProfileImageAsync(publishContext);
    }

    /// <summary>
    /// Removes the previously-published <c>public_image.json</c> artifact when there's no photo and no
    /// usable name or domain to fall back to (see <see cref="PublishFallbackProfileImageAsync"/>) --
    /// in practice only reachable for a null/empty tenant domain. Without this, <c>/pub/image</c> would
    /// keep serving the last-published bytes forever, since unlike <see cref="RepublishSiteDataAsync"/>
    /// (which rebuilds its whole document from scratch on every call and so naturally reflects a deletion as
    /// an empty section) there is nothing to rebuild *from* once the source attribute is gone.
    /// </summary>
    private async Task ClearProfileImageAsync(IOdinContext publishContext)
    {
        await staticFileContentService.ClearStaticFileAsync(StaticFileConstants.ProfileImageFileName);

        await mediator.Publish(new PublicProfileContentPublishedNotification
        {
            Artifact = PublicProfileArtifact.ProfileImage,
            OdinContext = publishContext
        });
    }

    //
    // public_profile.json
    //

    private async Task RepublishProfileCardAsync(IOdinContext publishContext)
    {
        var nameResults = await QueryAnonymousAttributesAsync([BuiltInProfileAttributes.Name], publishContext, maxRecords: 1);
        var nameContent = DeserializeAttributeContent(nameResults.FirstOrDefault()?.FileMetadata.AppData.Content);

        var statusContent = DeserializeAttributeContent((await QueryAnonymousAttributesAsync(
                [BuiltInProfileAttributes.Status], publishContext, maxRecords: 1))
            .FirstOrDefault()?.FileMetadata.AppData.Content);

        var bio = (await QueryAnonymousAttributesAsync([BuiltInProfileAttributes.Bio], publishContext))
            .Select(a => GetDataString(DeserializeAttributeContent(a.FileMetadata.AppData.Content), ProfileAttributeFields.ShortBio))
            .FirstOrDefault(s => !string.IsNullOrEmpty(s));

        var bioSummary = (await QueryAnonymousAttributesAsync([BuiltInProfileAttributes.BioSummary], publishContext))
            .Select(a => GetDataString(DeserializeAttributeContent(a.FileMetadata.AppData.Content), ProfileAttributeFields.ShortBio))
            .FirstOrDefault(s => !string.IsNullOrEmpty(s));

        var emails = (await QueryAnonymousAttributesAsync([BuiltInProfileAttributes.Email], publishContext))
            .Select(a => DeserializeAttributeContent(a.FileMetadata.AppData.Content))
            .Select(c => new FrontEndProfileEmail
            {
                Type = GetDataString(c, ProfileAttributeFields.Label),
                Email = GetDataString(c, ProfileAttributeFields.Email)
            })
            .Where(e => !string.IsNullOrEmpty(e.Type) && !string.IsNullOrEmpty(e.Email))
            .ToList();

        // Mirrors ProfileCardManager.ts's social-link construction: it doesn't hardcode a per-platform field
        // key either -- it just takes whatever the (single) key in the attribute's data dict happens to be
        // and feeds it straight into the URL template below.
        var socialLinks = (await QueryAnonymousAttributesAsync(SocialAndGameTypes.Select(t => t.Type), publishContext))
            .Select(a => DeserializeAttributeContent(a.FileMetadata.AppData.Content))
            .Select(c => c?.Data?.Count > 0 ? c.Data.First() : (KeyValuePair<string, object>?)null)
            .Where(kvp => kvp is { Key.Length: > 0 })
            .Select(kvp => new FrontEndProfileLink
                { Type = kvp!.Value.Key, Url = GetSocialLink(kvp.Value.Key, Convert.ToString(kvp.Value.Value)) })
            .Where(l => !string.IsNullOrEmpty(l.Url))
            .ToList();

        var links = (await QueryAnonymousAttributesAsync([BuiltInProfileAttributes.Link], publishContext))
            .Select(a => DeserializeAttributeContent(a.FileMetadata.AppData.Content))
            .Select(c => new FrontEndProfileLink
            {
                Type = GetDataString(c, ProfileAttributeFields.LinkText),
                Url = GetDataString(c, ProfileAttributeFields.LinkTarget)
            })
            .Where(l => !string.IsNullOrEmpty(l.Type) && !string.IsNullOrEmpty(l.Url))
            .ToList();

        var displayName = GetDataString(nameContent, ProfileAttributeFields.DisplayName);

        var profile = new FrontEndProfile
        {
            Name = string.IsNullOrEmpty(displayName) ? publishContext.Tenant.ToString() : displayName,
            GiveName = GetDataString(nameContent, ProfileAttributeFields.GivenName),
            FamilyName = GetDataString(nameContent, ProfileAttributeFields.Surname),
            Status = GetDataString(statusContent, ProfileAttributeFields.Status),
            Bio = bio ?? "",
            BioSummary = bioSummary ?? "",
            Image = $"https://{publishContext.Tenant}/pub/image",
            Email = emails,
            Links = socialLinks.Concat(links).ToList(),
            SameAs = socialLinks
        };

        var json = OdinSystemSerializer.Serialize(profile);
        await staticFileContentService.PublishProfileCardAsync(json);

        await mediator.Publish(new PublicProfileContentPublishedNotification
        {
            Artifact = PublicProfileArtifact.ProfileCard,
            OdinContext = publishContext
        });
    }

    // odin-js ProfileConfig.ts getSocialLink, ported verbatim -- it doesn't need to know the per-platform
    // field key convention either, since it operates purely on whatever key/value pair is already stored.
    private static readonly HashSet<string> UnlinkableSocialTypes = new(StringComparer.OrdinalIgnoreCase)
        { "minecraft", "steam", "discord", "riot games", "epic games", "stackoverflow" };

    private static string GetSocialLink(string type, string value)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value) || UnlinkableSocialTypes.Contains(type))
        {
            return null;
        }

        if (type.Equals("dotyouid", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{value}";
        }

        var prefix = type.Equals("linkedin", StringComparison.OrdinalIgnoreCase) ? "in/"
            : type.Equals("snapchat", StringComparison.OrdinalIgnoreCase) ? "add/"
            : "";
        return $"https://{type}.com/{prefix}{value}";
    }

    //
    // shared query/content helpers
    //

    private async Task<List<SharedSecretEncryptedFileHeader>> QueryAnonymousAttributesAsync(
        IEnumerable<Guid> attributeTypes, IOdinContext publishContext, int maxRecords = 100)
    {
        // Filtering by the attribute-type tag alone is sufficient to disambiguate -- a built-in type maps
        // 1:1 to its section, so a redundant GroupId filter here only makes this fragile to writers (e.g.
        // test fixtures) that set Tags but not GroupId, without adding any real safety.
        var qp = new FileQueryParamsV1
        {
            TargetDrive = ProfileDrive,
            FileType = [ProfileAttributeService.AttributeFileType],
            TagsMatchAtLeastOne = attributeTypes
        };

        var options = new QueryBatchResultOptions
        {
            MaxRecords = maxRecords,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false
        };

        var batch = await fileSystem.Query.GetBatch(ProfileDrive.Alias, qp, options, publishContext);

        // Same "is this actually public" rule StaticFileContentService applies when serving sitedata.json --
        // reuse it directly so the two can't drift on what counts as public.
        return staticFileContentService.Filter(batch.SearchResults).ToList();
    }

    private ProfileAttributeContent DeserializeAttributeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return OdinSystemSerializer.Deserialize<ProfileAttributeContent>(content);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to deserialize profile attribute content while republishing static files");
            return null;
        }
    }

    private static string GetDataString(ProfileAttributeContent content, string key)
    {
        if (content?.Data == null || !content.Data.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        var s = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}