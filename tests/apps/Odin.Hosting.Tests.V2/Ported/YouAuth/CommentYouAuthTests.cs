using NUnit.Framework;

namespace Odin.Hosting.Tests.V2.Ported.YouAuth;

/// <summary>
/// Port of <c>YouAuthApi/Drive/CommentYouAuthTests</c>. Two placeholders for commenting as a YouAuth
/// caller, neither of which has ever had a body.
/// </summary>
/// <remarks>
/// The <c>[Ignore]</c> reason — "Need api to login via youauth in unit tests" — no longer holds in
/// this framework: <c>GuestSession</c> / <c>CallerSpec.Guest</c> perform exactly that login, and
/// several ported fixtures drive guest callers through it. The ignores are nevertheless carried
/// verbatim, because a port is a move: writing the two missing tests is new coverage and belongs in
/// its own change, where it can be reviewed as such.
/// </remarks>
[TestFixture]
public class CommentYouAuthTests : V2Fixture
{
    [Test]
    [Ignore("Need api to login via youauth in unit tests")]
    public void CanUploadCommentFromYouAuth()
    {
        Assert.Inconclusive("Need api to login via youauth in unit tests");
    }

    [Test]
    [Ignore("Need api to login via youauth in unit tests")]
    public void FailToUploadStandardFileFromYouAuth()
    {
        Assert.Inconclusive("Need api to login via youauth in unit tests");
    }
}
