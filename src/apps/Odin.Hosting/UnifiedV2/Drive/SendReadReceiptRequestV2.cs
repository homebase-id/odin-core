using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive;

public class SendReadReceiptRequestV2 
{
    public List<Guid> Files { get; init; } = [];
}