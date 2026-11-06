#nullable enable
using System.Collections.Generic;

namespace Odin.Services.Drives;

public static class BuiltInDriveAttributes
{
    public const string IsCollaborativeChannel = "IsCollaborativeChannel";

    /// <summary>Whether <paramref name="attribute"/> is set to a value that parses as true.</summary>
    public static bool IsTrue(IReadOnlyDictionary<string, string>? attributes, string attribute) =>
        attributes != null && attributes.TryGetValue(attribute, out var value) && bool.TryParse(value, out var flag) && flag;

    public static bool IsCollaborativeChannelIn(IReadOnlyDictionary<string, string>? attributes) =>
        IsTrue(attributes, IsCollaborativeChannel);
}
