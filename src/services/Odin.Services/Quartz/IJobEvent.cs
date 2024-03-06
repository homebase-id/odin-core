using System;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Services.Quartz;

public interface IJobEvent
{
    Task Execute(IJobExecutionContext context, JobStatus status);
}
