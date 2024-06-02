using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Core.Tests.Util;

public class ConcurrentFileManagerTests
{
    private readonly ILogEventMemoryStore _logStore;
    private readonly ILogger<ConcurrentFileManager> _logger;

    private readonly CorrelationContext _correlationContext = new(new CorrelationUniqueIdGenerator());
    private ConcurrentFileManager _fileManager;

    public ConcurrentFileManagerTests()
    {
        _logStore = new LogEventMemoryStore();
        _logger = TestLogFactory.CreateConsoleLogger<ConcurrentFileManager>(_logStore, LogEventLevel.Verbose);
    }

    [SetUp]
    public void Setup()
    {
        _logStore.Clear();
        _fileManager = new ConcurrentFileManager(_logger, _correlationContext);
    }

    [TearDown]
    public void TearDown()
    {
        var logEvents = _logStore.GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(0), "Unexpected number of Error log events");
        Assert.That(logEvents[LogEventLevel.Fatal].Count, Is.EqualTo(0), "Unexpected number of Fatal log events");
    }

    [Test]
    public void ReadFile_FileExists_ContentReadCorrectly()
    {
        string testFilePath = "testfile01.txt";
        File.WriteAllText(testFilePath, string.Empty);

        string expectedContent = "Hello, world!";
        File.WriteAllText(testFilePath, expectedContent);

        string actualContent = null;
        _fileManager.ReadFile(testFilePath, path => actualContent = File.ReadAllText(path));

        Assert.AreEqual(expectedContent, actualContent);
    }

    [Test]
    public void ReadFile_Counters()
    {
        var cfm = new ConcurrentFileManager(_logger, _correlationContext);
        string testFilePath = "testfile101.txt";
        string testFilePath2 = "testfile102.txt";
        File.WriteAllText(testFilePath, string.Empty);

        Assert.IsTrue(cfm._dictionaryLocks.Count == 0);

        string actualContent = null;
        cfm.ReadFile(testFilePath, path =>
        {
            Assert.IsTrue(cfm._dictionaryLocks.Count == 1);
            actualContent = File.ReadAllText(path);

            var innerTask = Task.Run(() =>
            {
                cfm.ReadFile(testFilePath2, innerPath =>
                {
                    Assert.IsTrue(cfm._dictionaryLocks.Count == 2);
                    // No need to read content here
                });
            });

            // Wait for the inner task to complete
            innerTask.Wait();
            Assert.IsTrue(cfm._dictionaryLocks.Count == 1);
        });

        Assert.IsTrue(cfm._dictionaryLocks.Count == 0);
    }


    [Test]
    public void ReadFile_Doubly()
    {
        string testFilePath = "testfile39.txt";
        File.WriteAllText(testFilePath, string.Empty);

        string expectedContent = "Hello, world!";
        File.WriteAllText(testFilePath, expectedContent);

        string actualContent1 = null;
        string actualContent2 = null;
        var innerTaskFinished = new ManualResetEvent(false);
        _fileManager.ReadFile(testFilePath, path =>
        {
            actualContent1 = File.ReadAllText(path);
            Task innerTask = Task.Run(() =>
            {
                _fileManager.ReadFile(testFilePath, innerPath => { actualContent2 = File.ReadAllText(innerPath); });
                innerTaskFinished.Set();
            });

            // Wait for the inner task to complete
            innerTaskFinished.WaitOne();
        });

        Assert.AreEqual(expectedContent, actualContent1);
        Assert.AreEqual(expectedContent, actualContent2);
    }

    [Test]
    public void ReadWhileWritingFail()
    {
        string testFilePath = "testfile40.txt";
        File.WriteAllText(testFilePath, string.Empty);

        string expectedContent = "Hello, world!";

        string actualContent1 = null;
        string actualContent2 = null;
        var innerTaskFinished = new ManualResetEvent(false);
        _fileManager.WriteFile(testFilePath, path =>
        {
            File.WriteAllText(path, expectedContent);
            actualContent1 = File.ReadAllText(path);
            Task innerTask = Task.Run(() =>
            {
                try
                {
                    _fileManager.ReadFile(testFilePath, innerPath => { actualContent2 = File.ReadAllText(innerPath); });
                    Assert.Fail("Not supposed to be here");
                }
                catch (Exception ex)
                {
                    Assert.IsTrue(ex.Message.Contains("No access, file is already being"), "The exception message does not contain the expected substring.");
                }
                finally
                {
                    innerTaskFinished.Set();
                }
            });

            // Wait for the inner task to complete
            innerTaskFinished.WaitOne();
        }).GetAwaiter().GetResult();

        Assert.AreEqual(expectedContent, actualContent1);
        Assert.AreEqual(actualContent2, null);
    }


    [Test]
    public void WriteWhileWritingFail()
    {
        string testFilePath = "testfile41.txt";
        File.WriteAllText(testFilePath, string.Empty);

        string expectedContent = "Hello, world!";

        string actualContent1 = null;
        string actualContent2 = null;
        var innerTaskFinished = new ManualResetEvent(false);
        _fileManager.WriteFile(testFilePath, path =>
        {
            File.WriteAllText(path, expectedContent);
            actualContent1 = File.ReadAllText(path);
            Task innerTask = Task.Run(() =>
            {
                try
                {
                    _fileManager.WriteFile(testFilePath, innerPath => { actualContent2 = File.ReadAllText(innerPath); }).GetAwaiter().GetResult();
                    Assert.Fail("Not supposed to be here");
                }
                catch (Exception ex)
                {
                    Assert.IsTrue(ex.Message.Contains("Timeout waiting for lock"), "The exception message does not contain the expected substring.");
                }
                finally
                {
                    innerTaskFinished.Set();
                }
            });

            // Wait for the inner task to complete
            innerTaskFinished.WaitOne();
        }).GetAwaiter().GetResult();

        Assert.AreEqual(expectedContent, actualContent1);
        Assert.AreEqual(actualContent2, null);
    }

    [Test]
    public void StreamDispose_ReleasesLock_AllowsWrite()
    {
        string testFilePath = "testfile42.txt";
        string newContent = "New content";
        var fileManager = new ConcurrentFileManager(_logger, _correlationContext);

        fileManager.WriteFile(testFilePath, path => File.WriteAllText(path, newContent)).GetAwaiter().GetResult();

        // Open a stream for reading
        var stream = fileManager.ReadStream(testFilePath).GetAwaiter().GetResult();

        // Attempt to write to the file in a separate task, which should fail due to the Stream's lock
        var writeTask = Task.Run(() =>
        {
            try
            {
                fileManager.WriteFile(testFilePath, path => { File.WriteAllText(path, newContent); }).GetAwaiter().GetResult();;
                Assert.Fail("Write operation should not succeed while the stream is open.");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(ex.Message.Contains("No access, file is already being"), "The exception message does not contain the expected substring.");
            }
        });

        // Wait for the write task to complete
        writeTask.Wait();

        // Dispose the stream, releasing the lock
        stream.Dispose();

        // Test that we can now write to the file after the stream is disposed
        bool writeSucceeded = false;
        try
        {
            fileManager.WriteFile(testFilePath, path =>
            {
                File.WriteAllText(path, newContent);
                writeSucceeded = true;
            }).GetAwaiter().GetResult();
        }
        catch
        {
            // If an exception is thrown here, the test should fail
        }

        Assert.IsTrue(writeSucceeded, "Write operation should succeed after the stream is disposed.");
    }

    [Test]
    public void WriteFile_CanWriteToFile_ContentWrittenCorrectly()
    {
        string testFilePath = "testfile02.txt";
        File.WriteAllText(testFilePath, string.Empty);

        string contentToWrite = "Sample content";

        _fileManager.WriteFile(testFilePath, path => File.WriteAllText(path, contentToWrite)).GetAwaiter().GetResult();;

        string actualContent = File.ReadAllText(testFilePath);
        Assert.AreEqual(contentToWrite, actualContent);
    }

    [Test]
    public void DeleteFile_FileExists_FileDeleted()
    {
        string testFilePath = "testfile03.txt";
        File.WriteAllText(testFilePath, string.Empty);

        _fileManager.DeleteFile(testFilePath).GetAwaiter().GetResult();;

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
            _fileManager.ReadFile(testFilePath, path =>
            {
                Assert.AreEqual(expectedContent, File.ReadAllText(path));
                Interlocked.Increment(ref readCount);
            });
        });

        Assert.AreEqual(numberOfReads, readCount);
    }


    [Test]
    public void MoveFile_FileMovedSuccessfully()
    {
        ConcurrentFileManager fileManager = new ConcurrentFileManager(_logger, _correlationContext);
        string sourceFilePath = "sourceFile1.txt";
        string destinationFilePath = "destinationFile1.txt";
        string expectedContent = "This is a test file.";

        File.Delete(destinationFilePath);

        // Ensure the source file exists and destination file does not exist
        File.WriteAllText(sourceFilePath, expectedContent);

        Assert.IsTrue(File.Exists(sourceFilePath), "Source file should exist before moving.");
        Assert.IsFalse(File.Exists(destinationFilePath), "Destination file should not exist before moving.");

        fileManager.MoveFile(sourceFilePath, destinationFilePath, (source, destination) => { File.Move(source, destination); });

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
        ConcurrentFileManager fileManager = new ConcurrentFileManager(_logger, _correlationContext);
        string sourceFilePath = "sourceFile2.txt";
        string destinationFilePath = "destinationFile2.txt";
        string expectedContent = "This is a test file.";

        // Ensure the source file exists and destination file does not exist
        File.WriteAllText(sourceFilePath, expectedContent);
        File.WriteAllText(destinationFilePath, "with other content");

        Assert.IsTrue(File.Exists(sourceFilePath), "Source file should exist before moving.");
        Assert.IsTrue(File.Exists(destinationFilePath), "file should exist before moving.");

        fileManager.MoveFile(sourceFilePath, destinationFilePath, (source, destination) => { File.Replace(source, destination, null); });

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