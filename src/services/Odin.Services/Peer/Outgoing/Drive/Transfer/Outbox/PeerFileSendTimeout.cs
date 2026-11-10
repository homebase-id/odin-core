using System;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

/// <summary>
/// How long a peer file send may take before the sender gives up: five minutes, plus its payloads' time at 1 MiB/s.
/// HttpClient's default of 100 s, which every other peer call keeps, cut off any file that couldn't be sent and stored
/// in that time, however fast it flowed (#1877).  The load balancers in front of identities cut a connection only after
/// 300 s with no bytes, never a busy one (read from the OVH API, not from this repo).
/// <para>
/// Capped so a send's every attempt (<see cref="OdinConfiguration.HostSection.PeerOperationMaxAttempts"/>) ends before the
/// outbox takes its item for dead and hands it to another worker
/// (<see cref="OdinConfiguration.HostSection.InboxOutboxRecoveryAgeSeconds"/>): six hours by default, which a file of 26 GiB
/// fills at 1.3 MiB/s.
/// </para>
/// </summary>
public static class PeerFileSendTimeout
{
    private static readonly TimeSpan Base = TimeSpan.FromMinutes(5);
    private const long SlowestBytesPerSecond = 1024 * 1024;

    public static TimeSpan For(ServerFileHeader header, OdinConfiguration.HostSection host)
    {
        var (payloadBytes, thumbBytes) = DriveStorageServiceBase.PayloadByteCount(header);
        var bySize = Base + TimeSpan.FromSeconds((double)(payloadBytes + thumbBytes) / SlowestBytesPerSecond);
        var cap = TimeSpan.FromSeconds(host.InboxOutboxRecoveryAgeSeconds) / (host.PeerOperationMaxAttempts + 1);
        return bySize < cap ? bySize : cap;
    }
}
