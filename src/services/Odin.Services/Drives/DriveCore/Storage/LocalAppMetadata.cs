using Odin.Core.Exceptions;
using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Metadata about the file which is never sent over peer
/// </summary>
public class LocalAppMetadata
{
    public static readonly int MaxTagCount = 50;
    public static readonly int MaxLocalAppDataContentLength = 4 * 1024;

    public Guid VersionTag { get; set; }

    /// <summary>
    /// Initialization vector used when the target file is encrypted.  Note the AesKey will be that of the file's KeyHeader
    /// </summary>
    public byte[] Iv { get; init; }

    public string Content { get; init; }

    public List<Guid> Tags { get; init; }


    public bool TryValidate()
    {
        try
        {
            Validate();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Validate()
    {
        if (Tags?.Count > MaxTagCount)
            throw new OdinClientException($"Too many Tags count {Tags.Count} in LocalAppMetaData max {MaxTagCount}");

        if (Content?.Length > MaxLocalAppDataContentLength)
            throw new OdinClientException($"Content length {Content.Length} in AppFileMetaData max {MaxLocalAppDataContentLength}", 
                OdinClientErrorCode.MaxContentLengthExceeded);
    }
}