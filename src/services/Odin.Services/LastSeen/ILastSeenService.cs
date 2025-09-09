using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Registry;

namespace Odin.Services.LastSeen;

#nullable enable

public interface ILastSeenService
{
    Task LastSeenNowAsync(OdinId odinId);
    Task LastSeenNowAsync(string subject);
    Task PutLastSeenAsync(OdinId odinId, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(string subject, UnixTimeUtc lastSeen);
    Task<UnixTimeUtc?> GetLastSeenAsync(OdinId odinId);
    Task<UnixTimeUtc?> GetLastSeenAsync(string subject);
}
