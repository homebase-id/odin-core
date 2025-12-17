using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class FileQueryParamsV2
{
    public Guid DriveId { get; set; }
    public IEnumerable<int> FileType { get; set; } = null;
  
    public IEnumerable<FileState> FileState { get; set; } = null;

    public IEnumerable<int> DataType { get; set; } = null;

    public IEnumerable<int> ArchivalStatus { get; set; } = null;

    public IEnumerable<string> Sender { get; set; } = null;

    public IEnumerable<Guid> GroupId { get; set; } = null;

    public UnixTimeUtcRange UserDate { get; set; } = null;

    public IEnumerable<Guid> ClientUniqueIdAtLeastOne { get; set; } = null;
    public IEnumerable<Guid> TagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<Guid> TagsMatchAll { get; set; } = null;
    
    public IEnumerable<Guid> LocalTagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<Guid> LocalTagsMatchAll { get; set; } = null;
    
    public IEnumerable<Guid> GlobalTransitId { get; set; }
    
    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotEmptyGuid(this.DriveId, "driveid");
    }
}

