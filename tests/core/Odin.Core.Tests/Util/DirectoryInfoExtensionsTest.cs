using System;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

public class DirectoryInfoExtensionsTest
{
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
    private readonly string _targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

    [SetUp]
    public void Setup()
    {
        CreateDirectoryTree(_sourcePath);
        Console.WriteLine(_sourcePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_sourcePath))
        {
            Directory.Delete(_sourcePath, true);
        }
        if (Directory.Exists(_targetPath))
        {
            Directory.Delete(_targetPath, true);
        }
    }

    [Test]
    public void ItShouldCopyTheDirectory()
    {
        var dirInfo = new DirectoryInfo(_sourcePath);
        dirInfo.CopyTo(_targetPath);
        ClassicAssert.IsTrue(File.Exists(Path.Combine(_targetPath, "file0.txt")));
        ClassicAssert.IsTrue(File.Exists(Path.Combine(_targetPath, "SubFolder1", "file1.txt")));
        ClassicAssert.IsTrue(File.Exists(Path.Combine(_targetPath, "SubFolder2", "file2.txt")));
        ClassicAssert.IsTrue(File.Exists(Path.Combine(_targetPath, "SubFolder2", "SubSubFolder1", "file3.txt")));
    }

    [Test]
    public void ItShouldFailCopyIfSourcDoesNotExist()
    {
        var dirInfo = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")));
        Assert.Throws<DirectoryNotFoundException>(() => dirInfo.CopyTo(_targetPath));
    }


    private static void CreateDirectoryTree(string rootPath)
    {
        // /rootPath
        //     file0.txt
        //     /SubFolder1
        //        file1.txt
        //     /SubFolder2
        //        file2.txt
        //        /SubSubFolder1
        //           file3.txt

        // Create root directory
        Directory.CreateDirectory(rootPath);

        // Create subdirectories
        var subFolder1 = Path.Combine(rootPath, "SubFolder1");
        var subFolder2 = Path.Combine(rootPath, "SubFolder2");
        Directory.CreateDirectory(subFolder1);
        Directory.CreateDirectory(subFolder2);

        // Create a sub-subdirectory
        var subSubFolder1 = Path.Combine(subFolder2, "SubSubFolder1");
        Directory.CreateDirectory(subSubFolder1);

        // Create files in the directories
        File.WriteAllText(Path.Combine(rootPath, "file0.txt"), "Contents of file0");
        File.WriteAllText(Path.Combine(subFolder1, "file1.txt"), "Contents of file1");
        File.WriteAllText(Path.Combine(subFolder2, "file2.txt"), "Contents of file2");
        File.WriteAllText(Path.Combine(subSubFolder1, "file3.txt"), "Contents of file3");
    }


}