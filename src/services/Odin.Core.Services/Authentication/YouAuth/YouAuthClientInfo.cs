using System;
using System.Runtime.Serialization;
using System.Text.Json;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthClientInfo
{
    public string? Name { get; set; }

    public string? Description { get; set; }
    // ...
    // ...
    // ...

    public string? Uri { get; set; } // Go here to fetch more details

}
