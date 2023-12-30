using NUnit.Framework;
using Odin.Core.Util;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

[TestFixture]
public class ConcurrentFileManagerTests
{
    private ConcurrentFileManager fileManager;

    [SetUp]
    public void Setup()
    {
        fileManager = new ConcurrentFileManager();
    }

    [TearDown]
    public void TearDown()
    {
    }

    [Test]
    public void ReadFile_FileExists_ContentReadCorrectly()
    {
        string testFilePath = "testfile01.txt";
        File.WriteAllText(testFilePath, string.Empty);

        string expectedContent = "Hello, world!";
        File.WriteAllText(testFilePath, expectedContent);

        string actualContent = null;
        fileManager.ReadFile(testFilePath, path => actualContent = File.ReadAllText(path));

        Assert.AreEqual(expectedContent, actualContent);
    }

    [Test]
    public void WriteFile_CanWriteToFile_ContentWrittenCorrectly()
    {
        string testFilePath = "testfile02.txt";
        File.WriteAllText(testFilePath, string.Empty);

        string contentToWrite = "Sample content";

        fileManager.WriteFile(testFilePath, path => File.WriteAllText(path, contentToWrite));

        string actualContent = File.ReadAllText(testFilePath);
        Assert.AreEqual(contentToWrite, actualContent);
    }

    [Test]
    public void DeleteFile_FileExists_FileDeleted()
    {
        string testFilePath = "testfile03.txt";
        File.WriteAllText(testFilePath, string.Empty);

        fileManager.DeleteFile(testFilePath);

        Assert.IsFalse(File.Exists(testFilePath));
    }

    [Test]
    public void ConcurrentRead_MultipleReads_FileReadCorrectly()
    {
        string testFilePath = "testfile04.txt";
        File.WriteAllText(testFilePath, string.Empty);

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
        string testFilePath = "testfile05.txt";
        File.WriteAllText(testFilePath, string.Empty);

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

    [Test]
    public void MoveFile_FileMovedSuccessfully()
    {
        ConcurrentFileManager fileManager = new ConcurrentFileManager();
        string sourceFilePath = "sourceFile1.txt";
        string destinationFilePath = "destinationFile1.txt";
        string expectedContent = "This is a test file.";

        File.Delete(destinationFilePath);

        // Ensure the source file exists and destination file does not exist
        File.WriteAllText(sourceFilePath, expectedContent);

        Assert.IsTrue(File.Exists(sourceFilePath), "Source file should exist before moving.");
        Assert.IsFalse(File.Exists(destinationFilePath), "Destination file should not exist before moving.");

        fileManager.MoveFile(sourceFilePath, destinationFilePath, (source, destination) =>
        {
            File.Move(source, destination);
        });

        Assert.IsFalse(File.Exists(sourceFilePath), "Source file should not exist after moving.");
        Assert.IsTrue(File.Exists(destinationFilePath), "Destination file should exist after moving.");

        string actualContent = File.ReadAllText(destinationFilePath);
        Assert.AreEqual(expectedContent, actualContent, "The content of the moved file should match the expected content.");

        // Clean up: Delete the files after the test
        if (File.Exists(sourceFilePath))
        {
            File.Delete(sourceFilePath);
        }

        if (File.Exists(destinationFilePath))
        {
            File.Delete(destinationFilePath);
        }
    }

    [Test]
    public void MoveFile_FileMovedOverwriteSuccessfully()
    {
        ConcurrentFileManager fileManager = new ConcurrentFileManager();
        string sourceFilePath = "sourceFile2.txt";
        string destinationFilePath = "destinationFile2.txt";
        string expectedContent = "This is a test file.";

        // Ensure the source file exists and destination file does not exist
        File.WriteAllText(sourceFilePath, expectedContent);
        File.WriteAllText(destinationFilePath, "with other content");

        Assert.IsTrue(File.Exists(sourceFilePath), "Source file should exist before moving.");
        Assert.IsTrue(File.Exists(destinationFilePath), "file should exist before moving.");

        fileManager.MoveFile(sourceFilePath, destinationFilePath, (source, destination) =>
        {
            File.Replace(source, destination, null);
        });

        Assert.IsFalse(File.Exists(sourceFilePath), "Source file should not exist after moving.");
        Assert.IsTrue(File.Exists(destinationFilePath), "Destination file should exist after moving.");

        string actualContent = File.ReadAllText(destinationFilePath);
        Assert.AreEqual(expectedContent, actualContent, "The content of the moved file should match the expected content.");

        // Clean up: Delete the files after the test
        if (File.Exists(sourceFilePath))
        {
            File.Delete(sourceFilePath);
        }

        if (File.Exists(destinationFilePath))
        {
            File.Delete(destinationFilePath);
        }
    }
}
