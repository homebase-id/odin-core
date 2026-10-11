using System;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Registry.PayloadMove;

#nullable enable

namespace Odin.Services.Tests.Registry.PayloadMove;

public class PayloadMoveStateTests
{
    // #1919: the state is saved in its job's row after every slice. A save that fails leaves the job Running, and
    // the orphan rescue runs the same slice again, forever. Every list it keeps must fit, at its worst.
    [Test]
    public void AFullStateFitsItsJobRow()
    {
        var state = new PayloadMoveState { BaseUrl = "https://" + new string('h', 245) + ".example", Credential = new string('c', 64) };
        state.StartFrom(long.MaxValue - 1);
        state.Files = state.Objects = state.Bytes = state.Skipped = long.MaxValue;
        for (var i = 0; i < PayloadMoveState.MaxRecordedFailures + 10; i++)
        {
            state.AddFailure(new string('f', 1000));
        }

        for (var i = 0; i < PayloadMoveState.MaxRecordedMissing; i++)
        {
            state.AddMissing(new PayloadObject(Guid.NewGuid(), Guid.NewGuid(), "pay_key_10", new UnixTimeUtcUnique(long.MaxValue - i),
                long.MaxValue, int.MaxValue, int.MaxValue));
        }

        state.AcceptRefusal = $"{state.Missing[0]} is at the source now: run --retry to fetch it";
        state.AcceptedMissingAt = UnixTimeUtc.Now();
        state.Status = PayloadMoveStatus.CompleteWithFailures;

        var job = new PayloadMoveJob(null!, null!, null!, null!, null!) { Data = state };
        var jobData = job.SerializeJobData()!;

        Assert.That(jobData.Length, Is.LessThan(60_000), "with room to spare under the row's 65,535");
        Assert.DoesNotThrow(() => new JobsRecord
        {
            id = Guid.NewGuid(),
            name = "payload-move",
            correlationId = "correlation",
            jobType = PayloadMoveJob.JobTypeId.ToString(),
            jobData = jobData,
            identityId = Guid.NewGuid(),
        }.Validate());

        job.DeserializeJobData(jobData);
        Assert.That(job.Data.Missing, Is.EqualTo(state.Missing), "a thumbnail's size survives the compact form");
        Assert.That(job.Data.Failures, Has.Count.EqualTo(PayloadMoveState.MaxRecordedFailures));
        Assert.That(job.Data.Failures.Max(f => f.Length), Is.LessThanOrEqualTo(PayloadMoveState.MaxFailureLength));
    }

    [Test]
    public void APayloadIsStoredWithoutAThumbnailSize()
    {
        var job = new PayloadMoveJob(null!, null!, null!, null!, null!);
        job.Data.AddMissing(new PayloadObject(Guid.NewGuid(), Guid.NewGuid(), "pay_key1", new UnixTimeUtcUnique(1), 10));

        var jobData = job.SerializeJobData()!;
        job.DeserializeJobData(jobData);

        Assert.That(jobData, Does.Not.Contain("width").And.Not.Contain("isThumbnail"));
        Assert.That(job.Data.Missing.Single().IsThumbnail, Is.False);
    }
}
