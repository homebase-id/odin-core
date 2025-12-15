using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.LinkPreview.PersonMetadata;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.LinkPreview.Profile;

/// <summary>
/// Loads information about this tenant and their profile information for server side rendering requirements
/// </summary>
public class HomebaseProfileContentService(
    StaticFileContentService staticFileContentService,
    StandardFileSystem fileSystem,
    IHttpContextAccessor httpContextAccessor,
    IDriveManager driveManager,
    ILogger<HomebaseProfileContentService> logger)
{
    public const int AttributeFileType = 77;
    public static readonly Guid AboutSectionId = new("fd2ddd8616b64814aaef168775757632");

    public string GetPublicImageUrl(IOdinContext odinContext)
    {
        var context = httpContextAccessor.HttpContext;
        var imageUrl = $"{context.Request.Scheme}://{odinContext.Tenant}/{LinkPreviewDefaults.PublicImagePath}";
        return imageUrl;
    }

    public async Task<PersonSchema> LoadPersonSchema()
    {
        // read the profile file.
        var (_, fileExists, bytes) = await staticFileContentService
            .GetStaticFileStreamAsync(StaticFileConstants.PublicProfileCardFileName);

        FrontEndProfile profile = null;

        if (fileExists && bytes is { Length: > 0 })
        {
            var s = Encoding.UTF8.GetString(bytes);
            profile = OdinSystemSerializer.DeserializeOrThrow<FrontEndProfile>(s);
        }

        var context = httpContextAccessor.HttpContext;
        string odinId = context.Request.Host.Host;

        var person = new PersonSchema
        {
            Name = profile?.Name,
            GivenName = profile?.GiveName,
            FamilyName = profile?.FamilyName,
            Email = null,
            Description = profile?.BioSummary ?? profile?.Bio,
            Bio = profile?.Bio,
            BioSummary = profile?.BioSummary,
            BirthDate = null,
            JobTitle = null,
            Image = AppendJpgIfNoExtension(profile?.Image ?? ""),
            Status = profile?.Status,
            SameAs = profile?.SameAs?.Select(s => s.Url).ToList() ?? [],
            Identifier =
            [
                $"{context.Request.Scheme}://{odinId}/.well-known/webfinger?resource=acct:@{odinId}",
                $"{context.Request.Scheme}://{odinId}/.well-known/did.json"
            ]
        };
        return person;
    }

    public async Task<List<FrontEndProfileLink>> LoadLinks()
    {
        // read the profile file.
        var (_, fileExists, bytes) = await staticFileContentService.GetStaticFileStreamAsync(
            StaticFileConstants.PublicProfileCardFileName);

        FrontEndProfile profile = null;

        if (fileExists && bytes is { Length: > 0 })
        {
            var s = Encoding.UTF8.GetString(bytes);
            profile = OdinSystemSerializer.DeserializeOrThrow<FrontEndProfile>(s);
        }

        return profile?.Links ?? [];
    }

    private static string AppendJpgIfNoExtension(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            return url;
        }

        string path = uri.AbsolutePath;

        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            string newPath = path + ".jpg";

            UriBuilder builder = new UriBuilder(uri)
            {
                Path = newPath
            };

            return builder.Uri.ToString();
        }

        return url;
    }

    public async Task<AboutSection> LoadAboutSection(IOdinContext odinContext)
    {
        var theDrive = await driveManager.GetDriveAsync(SystemDriveConstants.ProfileDrive.Alias);
        if (null == theDrive)
        {
            return null;
        }

        var qp = new FileQueryParams
        {
            TargetDrive = SystemDriveConstants.ProfileDrive,
            FileType = [AttributeFileType],
            GroupId = [AboutSectionId]
        };

        var options = new QueryBatchResultOptions
        {
            MaxRecords = 100,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
            IncludeTransferHistory = false,
            Ordering = QueryBatchSortOrder.Default,
            Sorting = QueryBatchSortField.FileId
        };

        var batch = await fileSystem.Query.GetBatch(SystemDriveConstants.ProfileDrive.Alias, qp, options, odinContext);
        var section = new AboutSection();

        string shortBioType = "1d89f51a-6e42-4074-8d6b-60916c0eec9a".ToLower();
        string statusType = "9acb44549b41563697bb490144ec6258".ToLower();
        string experienceType = "65635623682c2fadd2767d424f53690f".ToLower();
        string bioType = "2cd30a58568dc333237944481aeb9ff1".ToLower();
        var targetDrive = SystemDriveConstants.ProfileDrive;
        foreach (var s in batch.SearchResults)
        {
            try
            {
                if (string.IsNullOrEmpty(s.FileMetadata.AppData.Content))
                {
                    continue;
                }

                if (TryDeserialize(s.FileMetadata.AppData.Content, out var attribute))
                {
                    if (attribute.Type.ToLower() == shortBioType)
                    {
                        if (attribute.Data.TryGetValue("short_bio", out var data))
                        {
                            section.ShortBio.Add(Convert.ToString(data));
                        }
                    }

                    if (attribute.Type.ToLower() == statusType)
                    {
                        if (attribute.Data.TryGetValue("status", out var data))
                        {
                            section.Status.Add(Convert.ToString(data));
                        }
                    }

                    if (attribute.Type.ToLower() == experienceType)
                    {
                        var exp = new ExperienceAttribute();
                        if (attribute.Data.TryGetValue("full_bio", out var fullBio) &&
                            attribute.Data.TryGetValue("short_bio", out var shortBio) &&
                            attribute.Data.TryGetValue("experience_link", out var experienceLink))
                        {
                            exp.Title = Convert.ToString(shortBio);
                            exp.Link = Convert.ToString(experienceLink);

                            if (attribute.Data.TryGetValue("experience_image", out var imageKey))
                            {
                                // logger.LogDebug("Post has usable thumbnail");

                                var driveId = targetDrive.Alias;
                                StringBuilder b = new StringBuilder();
                                b.Append($"&payloadKey={imageKey}");
                                b.Append($"&width={400}&height={400}");
                                b.Append($"&lastModified={s.FileMetadata.Updated.milliseconds}");
                                b.Append($"&xfst=Standard"); // note: No comment support
                                b.Append($"&iac=true");

                                var builder = new UriBuilder("https", odinContext.Tenant)
                                {
                                    Path = $"api/v2/drives/{driveId}/files/{s.FileId}/thumb",
                                    Query = b.ToString()
                                };

                                exp.ImageUrl = builder.ToString();
                            }

                            exp.Description = Convert.ToString(fullBio);
                            section.Experience.Add(exp);
                        }
                    }

                    if (attribute.Type.ToLower() == bioType)
                    {
                        if (attribute.Data.TryGetValue("short_bio", out var data))
                        {
                            section.Bio.Add(Convert.ToString(data));
                        }
                    }
                }
                // else
                // {
                //     // dump raw
                //     logger.LogDebug("Could not deserialize ssr about section profile attribute.  content:[{content}]",
                //         s.FileMetadata.AppData.Content);
                // }
            }
            catch (Exception e)
            {
                logger.LogError(e, "could not deserialize ssr about section profile attribute");
            }
        }

        return section;
    }

    private bool TryDeserialize(string content, out ProfileBlock profile)
    {
        try
        {
            profile = OdinSystemSerializer.Deserialize<ProfileBlock>(content);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to deserialize profile block");
            profile = null;
        }

        return false;
    }
}