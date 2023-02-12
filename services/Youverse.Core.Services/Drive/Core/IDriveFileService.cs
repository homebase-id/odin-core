using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;

namespace Youverse.Core.Services.Drive.Core;

/// <summary>
/// Interface that composites the Query and Storage into a single class for simplicity
/// </summary>
public interface IDriveCoreService<TStorageService>  : IDisposable
    where TStorageService : DriveStorageServiceBase
{
    public DriveQueryServiceBase<TStorageService> Query { get; }

    public TStorageService Storage { get; }

    Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients);
}

