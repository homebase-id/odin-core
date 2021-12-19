using System;
using System.IO;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Information about a drive
    /// </summary>
    public sealed class StorageDrive
    {
        public Guid Id { get; init; }
        
        public string RootPath { get; init; }
        
    }
}