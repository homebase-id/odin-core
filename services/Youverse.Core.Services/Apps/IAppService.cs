using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    /// <summary>
    /// App specific functions like retrieving file headers
    /// </summary>
    public interface IAppService
    {
        Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients);
    }
}