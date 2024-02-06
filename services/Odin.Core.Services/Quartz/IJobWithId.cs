using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public interface IJobWithId : IJob
{
    string JobId { get; }
}
