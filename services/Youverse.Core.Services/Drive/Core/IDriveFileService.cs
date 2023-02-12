using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;

namespace Youverse.Core.Services.Drive.Core;

/// <summary>
/// Interface that composites the Query and Storage for a specific type of file (i.e. comment or standard)
/// </summary>
public interface IDriveFileService
{
    public IDriveQueryService Query { get; }

    public IDriveService Storage { get; }

    Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients);
}

