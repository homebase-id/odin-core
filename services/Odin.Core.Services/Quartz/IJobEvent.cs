using System;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;

public interface IJobEvent
{
    Task Execute(IServiceProvider serviceProvider, IJobExecutionContext context, JobStatus status);
}
