using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Services.Drives.Management;

public class CreateDriveRequest
{
    public string Name { get; set; }
    public TargetDrive TargetDrive { get; set; }
    public string Metadata { get; set; }
    public bool AllowAnonymousReads { get; set; }

    public bool AllowSubscriptions { get; set; }

    /// <summary>
    /// Specifies if the CDN may read this drive's payloads. Opt-in: a caller that omits this
    /// gets a drive the CDN cannot read. The Public Posts system drive is seeded with it set
    /// (see <see cref="SystemDriveConstants"/>); everything else is the owner's choice.
    /// </summary>
    public bool AllowCdn { get; set; }

    /// <summary>
    /// Every payload written to the drive must carry client-computed hashes (#1895)
    /// </summary>
    public bool RequirePayloadHashes { get; set; }

    public bool OwnerOnly { get; set; }

    /// <summary>
    /// The app that owns the drive.  Omitting it means the owner acting as themselves, which is the
    /// owner-console app -- never nobody.
    /// </summary>
    /// <remarks>
    /// Taken on trust: the app is stored, not resolved, and does not have to be registered yet.  It
    /// cannot be -- a registration is granted its drives, and a grant cannot be issued for a drive that
    /// is not there, so an app's drives are created before the app exists.  Naming an app nobody ever
    /// registers leaves the drive addressed at nothing until one does; <c>reassign-owner</c> is the way
    /// back from that, and it does check.
    /// <para>
    /// Ownership is half the drive's wire address (<c>/apps/{appSlug}/drives/{driveSlug}</c>) and is not
    /// changeable afterwards except by adoption or reassignment.
    /// </para>
    /// </remarks>
    public Guid? AppId { get; set; }

    /// <summary>
    /// The drive's portable name, unique per owning app.  Required (#1901): the server never derives
    /// one, and a missing or malformed slug is a 400.
    /// </summary>
    public OdinSlug DriveSlug { get; set; }

    /// <summary>
    /// Readable form of the drive's type, e.g. <c>channel</c>.  Required (#1901); a missing or
    /// malformed one is a 400.
    /// </summary>
    public OdinSlug DriveTypeSlug { get; set; }

    public Dictionary<string, string> Attributes { get; set; }

    /// <summary>
    /// What the app asked the drive to be (docs/zero-knowledge-drive-api-plan.md, step 4): Collaborative when it
    /// said nothing, or Private or SelectiveShare, made zero knowledge here and never converted later.
    /// </summary>
    public DriveSharingModel SharingModel { get; set; }

    /// <summary>With a zero-knowledge <see cref="SharingModel"/>: the drive's keys, made in the owner's browser.</summary>
    public ZkConvertDriveRequest ZkKeys { get; set; }

    /// <summary>Whether the drive is created zero knowledge; refuses a request whose model and keys disagree.</summary>
    public bool AssertSharingModel()
    {
        if (!SharingModel.IsZeroKnowledge())
        {
            if (SharingModel != DriveSharingModel.Collaborative)
            {
                throw new OdinClientException($"A drive is created Collaborative, Private or SelectiveShare, not {SharingModel}",
                    OdinClientErrorCode.ArgumentError);
            }

            if (ZkKeys != null)
            {
                throw new OdinClientException($"{nameof(ZkKeys)} are for a Private or SelectiveShare drive",
                    OdinClientErrorCode.ArgumentError);
            }

            return false;
        }

        if (ZkKeys == null)
        {
            throw new OdinClientException($"A {SharingModel} drive needs {nameof(ZkKeys)}, made in the owner's browser",
                OdinClientErrorCode.ArgumentError);
        }

        ZkKeys.Validate();

        // The server can't open what such a drive holds, so nothing may read it on the server's behalf
        if (AllowAnonymousReads || AllowCdn)
        {
            throw new OdinClientException($"A {SharingModel} drive allows neither anonymous reads nor the CDN",
                OdinClientErrorCode.ArgumentError);
        }

        return true;
    }
}