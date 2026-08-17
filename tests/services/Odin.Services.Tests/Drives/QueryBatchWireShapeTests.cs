using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Drives;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// Guard rail on the scoping constraint of issue #1629: the per-section status fields are V2-only.
///
/// <see cref="QueryBatchResponse"/> is the return type of ~17 single-query endpoints (V1 and V2) *and*
/// separately the element type of the V1 <see cref="QueryBatchCollectionResponse"/>. Adding the new fields
/// there instead of to <see cref="QueryBatchCollectionSectionV2"/> would change the V1 wire shape and the
/// shape of every single-query endpoint at once — which is exactly the mistake these tests exist to catch.
/// </summary>
public class QueryBatchWireShapeTests
{
    private static readonly string[] V2OnlyFields = ["status", "errorMessage", "errorCode"];

    [Test]
    public void QueryBatchResponseCarriesNoV2StatusFields()
    {
        var keys = SerializedKeys(new QueryBatchResponse());

        foreach (var field in V2OnlyFields)
        {
            Assert.That(keys, Does.Not.Contain(field),
                $"'{field}' belongs on {nameof(QueryBatchCollectionSectionV2)}, not on the shared " +
                $"{nameof(QueryBatchResponse)} — adding it here changes ~17 single-query endpoints and the " +
                "V1 collection at the same time.");
        }
    }

    [Test]
    public void QueryBatchResponseKeepsNameAndInvalidDrive()
    {
        // These look like dead weight on a single-query response, but the V1 collection still uses
        // QueryBatchResponse as its element type and populates both. They cannot be removed while V1 exists.
        var keys = SerializedKeys(new QueryBatchResponse());

        Assert.That(keys, Does.Contain("name"));
        Assert.That(keys, Does.Contain("invalidDrive"));
    }

    [Test]
    public void V1CollectionElementTypeIsUnchangedQueryBatchResponse()
    {
        var response = new QueryBatchCollectionResponse();
        response.Results.Add(QueryBatchResponse.FromInvalidDrive("s1"));

        var section = JsonDocument.Parse(OdinSystemSerializer.Serialize(response))
            .RootElement.GetProperty("results")[0];

        foreach (var field in V2OnlyFields)
        {
            Assert.That(section.TryGetProperty(field, out _), Is.False,
                $"the V1 collection payload must not grow a '{field}' key");
        }

        Assert.That(section.GetProperty("invalidDrive").GetBoolean(), Is.True,
            "invalidDrive keeps its V1 meaning");
    }

    [Test]
    public void V2SectionCarriesTheStatusFields()
    {
        // The other half of the constraint: the new fields have to live somewhere, and this is where.
        var keys = SerializedKeys(new QueryBatchCollectionSectionV2());

        foreach (var field in V2OnlyFields)
        {
            Assert.That(keys, Does.Contain(field));
        }

        Assert.That(keys, Does.Contain("invalidDrive"),
            "the legacy flag stays populated on V2 sections so a client reading only it still works");
    }

    [Test]
    public void V2SectionStatusSerializesAsCamelCaseString()
    {
        // The Kotlin client matches on the string form; a numeric enum would silently break it.
        var section = QueryBatchCollectionSectionV2.BudgetExhausted("s1", "cursor-abc");

        var json = JsonDocument.Parse(OdinSystemSerializer.Serialize(section)).RootElement;

        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("budgetExhausted"));
        Assert.That(json.GetProperty("cursorState").GetString(), Is.EqualTo("cursor-abc"),
            "the submitted cursor is echoed verbatim; the client re-sends it next round");
        Assert.That(json.GetProperty("hasMoreRows").GetBoolean(), Is.True);
    }

    [Test]
    public void V1CollectionRequestHasNoCollectionLevelMaxRecords()
    {
        // The record budget is V2-only. V1 keeps per-section budgets.
        var keys = typeof(QueryBatchCollectionRequest).GetProperties().Select(p => p.Name);

        Assert.That(keys, Does.Not.Contain(nameof(QueryBatchCollectionRequestV2.MaxRecords)));
    }

    [TestCase(1, 1)]
    [TestCase(100, 100)]
    [TestCase(V2BatchCollectionQueryService.MaxRecordCeiling, V2BatchCollectionQueryService.MaxRecordCeiling)]
    [TestCase(V2BatchCollectionQueryService.MaxRecordCeiling + 1, V2BatchCollectionQueryService.MaxRecordCeiling)]
    [TestCase(100_000, V2BatchCollectionQueryService.MaxRecordCeiling)]
    [TestCase(int.MaxValue, V2BatchCollectionQueryService.MaxRecordCeiling)]
    public void RecordBudgetIsClampedToTheCeiling(int requested, int expected)
    {
        // Clamped rather than rejected: an over-large ask just pages through via hasMoreRows.
        Assert.That(V2BatchCollectionQueryService.ClampRecordBudget(requested), Is.EqualTo(expected));
    }

    private static string[] SerializedKeys(object value) =>
        JsonDocument.Parse(OdinSystemSerializer.Serialize(value))
            .RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToArray();
}
