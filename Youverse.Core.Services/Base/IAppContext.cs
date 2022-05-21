using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public interface IAppContext
    {
        Guid AppId { get; }

        string AppName { get; }
        
    }
}