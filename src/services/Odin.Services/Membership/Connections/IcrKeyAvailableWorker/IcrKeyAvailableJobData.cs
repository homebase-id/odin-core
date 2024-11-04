#nullable enable

using Odin.Core.Identity;

namespace Odin.Services.Membership.Connections.IcrKeyAvailableWorker;

public class IcrKeyAvailableJobData
{
    public JobTokenType TokenType { get; init; }
    
    public OdinId? Tenant { get; init; }

    public byte[]? Iv { get; init; }

    public byte[]? EncryptedToken { get; init; }

    public enum JobTokenType
    {
        App = 1,
        Owner = 2
    }
}