using System;
using Autofac;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Registry.Registration;
using Odin.Services.ShamiraPasswordRecovery;

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
        jobTypeRegistry.RegisterJobType<SendRecoveryModeVerificationEmailJob>(cb, SendRecoveryModeVerificationEmailJob.JobTypeId);
        jobTypeRegistry.RegisterJobType<SendEmailJob>(cb, SendEmailJob.JobTypeId);

        //
        // Deprecated job types here.
        // Type must be DeprecatedJob
        // Id must be the JobType ID of the job type that no longer exists.
        //

        // Old IcrKeyAvailableJob
        jobTypeRegistry.RegisterJobType<DeprecatedJob>(cb, Guid.Parse("59d25227-25c1-4b26-b1e6-c50612eb15e3"));

        return cb;
    }
}