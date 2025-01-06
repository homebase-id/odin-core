using System;
using Autofac;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Membership.Connections.IcrKeyAvailableWorker;
using Odin.Services.Registry.Registration;

namespace Odin.Services.JobManagement;

public static class JobExtensions
{
    public static ContainerBuilder AddJobManagerServices(this ContainerBuilder cb)
    {
        cb.RegisterType<JobManager>()
            .As<IJobManager>()
            .InstancePerDependency();

        var jobTypeRegistry = new JobTypeRegistry();
        cb.RegisterInstance(jobTypeRegistry)
            .As<IJobTypeRegistry>()
            .SingleInstance();

        //
        // Register active jobs here.
        //

        jobTypeRegistry.RegisterJobType<ExportTenantJob>(cb, ExportTenantJob.JobTypeId);
        jobTypeRegistry.RegisterJobType<DeleteTenantJob>(cb, DeleteTenantJob.JobTypeId);
        jobTypeRegistry.RegisterJobType<SendProvisioningCompleteEmailJob>(cb, SendProvisioningCompleteEmailJob.JobTypeId);
        jobTypeRegistry.RegisterJobType<VersionUpgradeJob>(cb, VersionUpgradeJob.JobTypeId);
        jobTypeRegistry.RegisterJobType<IcrKeyAvailableJob>(cb, IcrKeyAvailableJob.JobTypeId);

        //
        // Deprecated job types here.
        // Type must be DeprecatedJob
        // Id must be the JobType ID of the job type that no longer exists.
        //

        // Example:
        jobTypeRegistry.RegisterJobType<DeprecatedJob>(cb, Guid.Parse("11111111-2222-3333-4444-555555555555"));

        return cb;
    }
}