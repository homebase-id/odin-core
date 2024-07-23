namespace Odin.Services.Background;


/*

Table Jobs
for "long running job" support

COLUMNS:
- id (uuid, not-null): primary key
- created (datetime, not-null): record created
- modified (datetime, allow-null): record modified
- state (enum, not-null): current state of the job (e.g. "running", "completed", "failed")
- priority (int, not-null): priority of the job
- nextRun (datetime, not-null): when to run next
- lastRun (datetime, allow-null): last run
- runCount (int, not-null): number of runs the job has had
- maxRuns (int, not-null): maximum number of runs the job can have before it is marked as failed
- onSuccessDeleteAfter (datetime, not-null): when to delete a job that has completed successfully
- onFailedDeleteAfter (datetime, not-null): when to delete a job that has failed
- correlationId (string, not-null): correlation id of the job (for log tracking)
- inputType (string, not-null): runtime type information of the job input
- inputData (json, not-null): serialized representation of the jobInputType instance
- inputHash (string, allow-null): hash of the job (optional, to control job duplication)
- outputType (string, not-null): runtime type information of the job output
- outputData (json, not-null): serialized representation of the jobOutputType instance

INDEXES:
- id (primary key)
- state (non-unique)
- priority (non-unique)
- nextRun (non-unique)
- inputHash (unique)

*/



public class BackgroundJob
{
    
}