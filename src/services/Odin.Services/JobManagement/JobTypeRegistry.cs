using System;
using System.Collections.Generic;
using Autofac;
using Odin.Core.Exceptions;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.JobManagement;

#nullable enable

public interface IJobTypeRegistry
{
    Type GetJobType(Guid jobTypeId);
}

public class JobTypeRegistry : IJobTypeRegistry
{
    private readonly Dictionary<Guid, Type> _jobTypes = new();

    //

    public void RegisterJobType<TJob>(ContainerBuilder cb, Guid jobTypeId) where TJob : AbstractJob
    {
        if (!_jobTypes.TryAdd(jobTypeId, typeof(TJob)))
        {
            throw new InvalidOperationException($"A registration with the job type id '{jobTypeId}' already exists.");
        }

        cb.RegisterType<TJob>().InstancePerDependency();
    }

    //

    public Type GetJobType(Guid jobTypeId)
    {
        if (_jobTypes.TryGetValue(jobTypeId, out var jobType))
        {
            return jobType;
        }
        throw new OdinSystemException($"Job type with ID {jobTypeId} is not registered");
    }
}
