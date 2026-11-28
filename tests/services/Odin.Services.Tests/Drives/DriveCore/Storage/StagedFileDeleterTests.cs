using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class StagedFileDeleterTests
{
    private string _folder = "";

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"staged-deleter-{Guid.NewGuid()}");
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task QueuedFilesAreDeletedAndOneThatCantBeIsSkipped()
    {
        using var deleter = new StagedFileDeleter(NullLogger<StagedFileDeleter>.Instance);
        var first = Touch("a.payload");
        var undeletable = Directory.CreateDirectory(Path.Combine(_folder, "a-directory.payload")).FullName;
        var missing = Path.Combine(_folder, "already-gone.payload");
        var last = Touch("b.payload");

        deleter.Enqueue([first, undeletable, missing, last]);
        await WaitUntilGoneAsync(last);

        Assert.That(File.Exists(first), Is.False, "the first queued file is deleted");
        Assert.That(Directory.Exists(undeletable), Is.True, "File.Delete can't remove a directory, so it stays");
        Assert.That(File.Exists(last), Is.False, "files queued after one that failed are still deleted");
    }

    [Test]
    public void FilesQueuedAfterTheHostStoppedAreLeft()
    {
        var deleter = new StagedFileDeleter(NullLogger<StagedFileDeleter>.Instance);
        deleter.Dispose();
        var file = Touch("late.payload");

        Assert.DoesNotThrow(() => deleter.Enqueue([file]), "enqueueing during shutdown is not an error");
        Assert.That(File.Exists(file), Is.True, "nothing deletes it now; the temp-folder clean-up does after the next start");
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private static async Task WaitUntilGoneAsync(string path)
    {
        var waited = Stopwatch.StartNew();
        while (File.Exists(path) && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(10);
        }

        Assert.That(File.Exists(path), Is.False, $"{path} still exists after {waited.Elapsed.TotalSeconds:0.0} s");
    }
}
