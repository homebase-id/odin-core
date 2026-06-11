#if RUN_S3_TESTS
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Hosting.Tests._Universal.DriveStorage;

/// <summary>
/// Characterization test that locks the behavior of the peer-transfer commit/promote
/// pipeline with S3 inbox + S3 payload storage (MinIO-backed). Exercises the full
/// S3 inbox -> S3 payload promote path.
///
/// This test is gated behind RUN_S3_TESTS because it requires a MinIO container
/// (started by WebScaffold's RUN_S3_TESTS block) and the S3 inbox config
/// would throw at startup without the underlying S3Storage being enabled.
/// </summary>
public class InboxPromotionS3CharacterizationTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(
            testIdentities: new List<TestIdentity> { TestIdentities.Frodo, TestIdentities.Samwise },
            envOverrides: new Dictionary<string, string>
            {
                // Under RUN_S3_TESTS, WebScaffold already enables S3Storage + S3Payload (MinIO);
                // this puts the inbox on S3 too, exercising the full S3 inbox -> S3 payload promote.
                ["S3Inbox__Enabled"] = "true",
                ["S3Inbox__BucketName"] = "odin-inbox",
            });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task PeerTransfer_WithPayloadAndThumbnail_CommitsToLongTerm_S3()
    {
        await InboxPromotionScenario.AssertEncryptedPeerTransferPromotesPayloadAndThumbnail(_scaffold);
    }
}
#endif
