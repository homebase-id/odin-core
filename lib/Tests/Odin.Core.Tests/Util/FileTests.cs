using NUnit.Framework;
using Odin.Core.Util;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

[TestFixture]
public class ConcurrentFileManagerTests
{
    private ConcurrentFileManager fileManager;
    private string testFilePath;

    [SetUp]
    public void Setup()
    {
        fileManager = new ConcurrentFileManager();
        testFilePath = "testfile.txt";

        // Ensure the test file exists and is empty before each test
        File.WriteAllText(testFilePath, string.Empty);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(testFilePath))
        {
            File.Delete(testFilePath);
        }
    }

    [Test]
    public void ReadFile_FileExists_ContentReadCorrectly()
    {
        string expectedContent = "Hello, world!";
        File.WriteAllText(testFilePath, expectedContent);

        string actualContent = null;
        fileManager.ReadFile(testFilePath, path => actualContent = File.ReadAllText(path));

        Assert.AreEqual(expectedContent, actualContent);
    }

    [Test]
    public void WriteFile_CanWriteToFile_ContentWrittenCorrectly()
    {
        string contentToWrite = "Sample content";

        fileManager.WriteFile(testFilePath, path => File.WriteAllText(path, contentToWrite));

        string actualContent = File.ReadAllText(testFilePath);
        Assert.AreEqual(contentToWrite, actualContent);
    }

    [Test]
    public void DeleteFile_FileExists_FileDeleted()
    {
        fileManager.DeleteFile(testFilePath);

        Assert.IsFalse(File.Exists(testFilePath));
    }

    [Test]
    public void ConcurrentRead_MultipleReads_FileReadCorrectly()
    {
        string expectedContent = "Concurrent read test";
        File.WriteAllText(testFilePath, expectedContent);

        int numberOfReads = 10;
        int readCount = 0;

        Parallel.For(0, numberOfReads, (i) =>
        {
            fileManager.ReadFile(testFilePath, path =>
            {
                Assert.AreEqual(expectedContent, File.ReadAllText(path));
                Interlocked.Increment(ref readCount);
            });
        });

        Assert.AreEqual(numberOfReads, readCount);
    }

    [Test]
    public void ReadBlockedDuringWrite()
    {
        string contentToWrite = "New content";
        string initialContent = "Old content";
        File.WriteAllText(testFilePath, initialContent);

        var writeLock = new ManualResetEvent(false);
        var readAttempted = false;
        var readCompleted = new ManualResetEvent(false);

        // Start a write operation in a separate task
        Task writeTask = Task.Run(() =>
        {
            fileManager.WriteFile(testFilePath, path =>
            {
                writeLock.Set(); // Signal that write has started
                Thread.Sleep(100); // Simulate a delay in writing
                File.WriteAllText(path, contentToWrite);
            });
        });

        // Attempt to read the file, this should block until the write is completed
        Task readTask = Task.Run(() =>
        {
            fileManager.ReadFile(testFilePath, path =>
            {
                readAttempted = true;
                string content = File.ReadAllText(path);
                Assert.AreEqual(contentToWrite, content); // This should be the new content if read is blocked during write
                readCompleted.Set();
            });
        });

        writeLock.WaitOne(); // Ensure the write operation has started
        Thread.Sleep(50); // Brief delay to increase the likelihood that read is attempted during write
        Assert.IsFalse(readAttempted, "Read operation should not have started yet.");

        readCompleted.WaitOne(); // Wait for the read operation to complete
        Assert.IsTrue(readAttempted, "Read operation should have completed.");

        Task.WaitAll(writeTask, readTask); // Ensure all tasks are completed
    }
}
