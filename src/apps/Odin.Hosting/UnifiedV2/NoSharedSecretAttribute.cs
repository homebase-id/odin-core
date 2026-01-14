#nullable enable
using System;

namespace Odin.Hosting.UnifiedV2;

/// <summary>
/// Controllers or actions with this attribute will not have shared secret request encryption applied
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class NoSharedSecretAttribute : Attribute
{
}