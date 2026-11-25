using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

/// <summary>
/// Adds and removes local tags (localAppData.tags) on many files, e.g.
/// <c>{ "addTags": ["…"], "files": [{ "fileId": "…" }] }</c>
/// </summary>
public class UpdateLocalTagsBatchRequestV2
{
    /// <summary>
    /// Tags every file gets; one it already has is left as it is
    /// </summary>
    public List<Guid> AddTags { get; init; }

    /// <summary>
    /// Tags every file loses; one it doesn't have is no error
    /// </summary>
    public List<Guid> RemoveTags { get; init; }

    /// <summary>
    /// The files to change, at most <see cref="FileVersionTagBatchV2.MaxFiles"/>
    /// </summary>
    public List<FileLocalVersionTagV2> Files { get; init; }

    /// <summary><see cref="AddTags"/>, empty when left out</summary>
    [JsonIgnore]
    public List<Guid> TagsToAdd => AddTags ?? [];

    /// <summary><see cref="RemoveTags"/>, empty when left out</summary>
    [JsonIgnore]
    public List<Guid> TagsToRemove => RemoveTags ?? [];

    /// <summary>
    /// A batch naming at least one tag, none empty, none both added and removed, and 1 to
    /// <see cref="FileVersionTagBatchV2.MaxFiles"/> files, none twice
    /// </summary>
    public void AssertValid()
    {
        FileVersionTagBatchV2.AssertValidBatch(Files);
        var add = TagsToAdd;
        var remove = TagsToRemove;
        OdinValidationUtils.AssertIsTrue(add.Count + remove.Count > 0, "addTags or removeTags must name a tag");
        OdinValidationUtils.AssertIsTrue(add.Distinct().Count() <= LocalAppMetadata.MaxTagCount &&
                                         remove.Distinct().Count() <= LocalAppMetadata.MaxTagCount,
            $"addTags and removeTags hold at most {LocalAppMetadata.MaxTagCount} tags each");
        OdinValidationUtils.AssertIsTrue(add.Concat(remove).All(t => t != Guid.Empty), "a tag may not be empty");
        OdinValidationUtils.AssertIsTrue(!add.Intersect(remove).Any(), "a tag may not be both added and removed");
    }
}

/// <summary>
/// One file of a local-tag batch, with the local version tag the caller last saw when the change is conditional
/// </summary>
public class FileLocalVersionTagV2 : IBatchFileV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// Optional. Given, the file is changed only if its local data is still at this version (Guid.Empty for a file whose
    /// local data was never written); otherwise its result is "versionTagMismatch". Left out, the tags are added and
    /// removed on top of the ones the file has.
    /// </summary>
    public Guid? LocalVersionTag { get; init; }
}

public class UpdateLocalTagsBatchResultV2
{
    /// <summary>
    /// One entry per requested file, in request order
    /// </summary>
    public List<UpdateLocalTagsResultV2> Results { get; init; }
}

public class UpdateLocalTagsResultV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// The file's local version tag after the change (unchanged when its tags already were as asked), for the next
    /// conditional local write; null when <see cref="ErrorCode"/> is set
    /// </summary>
    public Guid? NewLocalVersionTag { get; init; }

    /// <summary>
    /// The file's modified time after this request's last change to it (its current time when nothing changed), as
    /// the change feed shows it; null when <see cref="ErrorCode"/> is set
    /// </summary>
    public UnixTimeUtc? Updated { get; init; }

    /// <summary>
    /// Null on success. Otherwise why this file was not changed, as a camelCase string: "versionTagMismatch" (stale
    /// localVersionTag, or other writes kept getting in between), "fileNotFound" (missing, or hidden from the caller by
    /// its ACL), "argumentError" (the file would hold more than <see cref="LocalAppMetadata.MaxTagCount"/> local tags),
    /// or any other client error the write raised. One file's error never stops the others.
    /// </summary>
    public OdinClientErrorCode? ErrorCode { get; init; }
}
