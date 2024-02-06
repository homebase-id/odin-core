using Quartz;
namespace Odin.Core.Services.Quartz;
#nullable enable

/// <summary>
/// IExclusive job is put in ExclusiveJobManager.
/// The "exclusiveness" is determined by the JobKey on IJob.
/// ExclusiveJobManager will not allow a duplicate key being added
/// before the first one is manually removed.
/// </summary>
public interface IExclusiveJob : IJobWithId
{
    IJobState State { get; }
    bool IsDone { get; }
}
