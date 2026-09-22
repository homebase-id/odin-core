using System;
using System.Collections.Generic;
using Odin.Services.Authorization.Apps;

namespace Odin.Hosting.Tests;

/// <summary>
/// A slug for a test app, derived from its id so it is unique without the test having to pick one.
/// </summary>
/// <remarks>
/// A slug is required at registration.  The generator rather than a hand-rolled substring: it owns the
/// length and format rules, and it returns the tree's slug for a built-in id, which a hand-rolled one
/// would collide with.
/// </remarks>
public static class TestAppSlug
{
    public static string For(Guid appId) => AppSlugGenerator.Generate(appId, null, new HashSet<string>());
}
