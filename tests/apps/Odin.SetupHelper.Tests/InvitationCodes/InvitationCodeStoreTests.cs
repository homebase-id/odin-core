using Odin.SetupHelper.InvitationCodes;

namespace Odin.SetupHelper.Tests.InvitationCodes;

public class InvitationCodeStoreTests
{
    private string _databasePath = "";
    private InvitationCodeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"invitation-codes-{Guid.NewGuid()}.db");
        _store = new InvitationCodeStore(_databasePath);
        await _store.InitializeAsync();
    }

    [TearDown]
    public void TearDown()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task ACodeCanBeUsedExactlyMaxUsesTimes()
    {
        var created = await _store.CreateAsync("frodo.dotyou.cloud", 2);

        Assert.That(await _store.TryUseAsync(created.Code), Is.True);
        Assert.That(await _store.TryUseAsync(created.Code), Is.True);
        Assert.That(await _store.TryUseAsync(created.Code), Is.False);

        var code = await _store.GetAsync(created.Code);
        Assert.That(code!.UsesLeft, Is.EqualTo(0));
    }

    [Test]
    public async Task AnUnknownCodeCannotBeUsed()
    {
        Assert.That(await _store.TryUseAsync("K7QXM2PD9FHW"), Is.False);
        Assert.That(await _store.GetAsync("K7QXM2PD9FHW"), Is.Null);
    }

    [Test]
    public async Task UndoGivesAUseBack()
    {
        var created = await _store.CreateAsync("frodo.dotyou.cloud", 1);
        Assert.That(await _store.TryUseAsync(created.Code), Is.True);

        await _store.UndoUseAsync(created.Code);

        Assert.That((await _store.GetAsync(created.Code))!.UsesLeft, Is.EqualTo(1));
        Assert.That(await _store.TryUseAsync(created.Code), Is.True);
    }

    [Test]
    public async Task UndoWithNoUsesChangesNothing()
    {
        var created = await _store.CreateAsync("frodo.dotyou.cloud", 3);

        await _store.UndoUseAsync(created.Code);

        Assert.That((await _store.GetAsync(created.Code))!.UsesLeft, Is.EqualTo(3));
    }

    [Test]
    public async Task ListReturnsOnlyTheOwnersCodesWithUsesLeft()
    {
        var first = await _store.CreateAsync("frodo.dotyou.cloud", 5);
        var second = await _store.CreateAsync("frodo.dotyou.cloud", 1);
        await _store.CreateAsync("sam.dotyou.cloud", 5);
        await _store.TryUseAsync(first.Code);

        var codes = await _store.ListByOwnerAsync("frodo.dotyou.cloud");

        Assert.That(codes.Select(c => c.Code), Is.EquivalentTo(new[] { first.Code, second.Code }));
        Assert.That(codes.Single(c => c.Code == first.Code).UsesLeft, Is.EqualTo(4));
        Assert.That(codes.Single(c => c.Code == second.Code).UsesLeft, Is.EqualTo(1));
    }

    [Test]
    public async Task ParallelUsesNeverExceedMaxUses()
    {
        var created = await _store.CreateAsync("frodo.dotyou.cloud", 5);

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => _store.TryUseAsync(created.Code))));

        Assert.That(results.Count(used => used), Is.EqualTo(5));
        Assert.That((await _store.GetAsync(created.Code))!.UsesLeft, Is.EqualTo(0));
    }
}
