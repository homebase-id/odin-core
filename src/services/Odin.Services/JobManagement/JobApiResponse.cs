using System;
using Odin.Core.Storage.SQLite.ServerDatabase;

namespace Odin.Services.JobManagement;

#nullable enable

public class JobApiResponse
{
    public Guid? JobId { get; set; }
    public JobState? State { get; set; }
    public string? Error { get; set; }
    public string? Data { get; set; }

}

