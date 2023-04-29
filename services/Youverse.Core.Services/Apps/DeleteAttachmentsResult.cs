using System;

namespace Youverse.Core.Services.Apps;

public class DeleteAttachmentsResult
{
    public Guid NewVersionTag { get; set; }
}

public class DeleteThumbnailResult
{
    public Guid NewVersionTag { get; set; }
}

public class DeletePayloadResult
{
    public Guid NewVersionTag { get; set; }
}