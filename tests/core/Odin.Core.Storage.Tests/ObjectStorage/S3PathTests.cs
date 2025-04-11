using NUnit.Framework;
using Odin.Core.Storage.ObjectStorage;

namespace Odin.Core.Storage.Tests.ObjectStorage;

#nullable enable

public class S3PathTests
{
    [Test]
    public void CombineShouldDoItsThing()
    {
        Assert.That(S3Path.Combine(), Is.EqualTo(""));
        Assert.That(S3Path.Combine("/"), Is.EqualTo(""));
        Assert.That(S3Path.Combine("/xxx"), Is.EqualTo("xxx"));
        Assert.That(S3Path.Combine("xxx/"), Is.EqualTo("xxx/"));
        Assert.That(S3Path.Combine("xxx", "yyy"), Is.EqualTo("xxx/yyy"));
        Assert.That(S3Path.Combine("xxx", "yyy", "/"), Is.EqualTo("xxx/yyy/"));
        Assert.That(S3Path.Combine("xxx/", "\\yyy"), Is.EqualTo("xxx/yyy"));
        Assert.That(S3Path.Combine("data", "folder/file"), Is.EqualTo("data/folder/file"));
        Assert.That(S3Path.Combine("/data/", "/folder/"), Is.EqualTo("data/folder/"));
        Assert.That(S3Path.Combine("xxx", "/"), Is.EqualTo("xxx/"));
        Assert.That(S3Path.Combine("", "/yyy"), Is.EqualTo("yyy"));
        Assert.That(S3Path.Combine("", "/yyy/"), Is.EqualTo("yyy/"));
        Assert.That(S3Path.Combine("/", "/yyy/"), Is.EqualTo("yyy/"));
        Assert.That(S3Path.Combine("xxx", "", "/yyy"), Is.EqualTo("xxx/yyy"));
        Assert.That(S3Path.Combine("", ""), Is.EqualTo(""));
        Assert.That(S3Path.Combine("/", "/"), Is.EqualTo(""));
        Assert.That(S3Path.Combine("//", "//"), Is.EqualTo(""));
    }

    [Test]
    public void ItShouldThrowOnBadFileName()
    {
        Assert.Throws<S3StorageException>(() => S3Path.AssertFileName(""));
        Assert.Throws<S3StorageException>(() => S3Path.AssertFileName("/"));
        Assert.Throws<S3StorageException>(() => S3Path.AssertFileName("foo/"));
        Assert.Throws<S3StorageException>(() => S3Path.AssertFileName("foo/bar/"));

        Assert.DoesNotThrow(() => S3Path.AssertFileName("foo"));
        Assert.DoesNotThrow(() => S3Path.AssertFileName("foo/bar"));
    }

    [Test]
    public void ItShouldThrowOnBadFolderName()
    {
        Assert.Throws<S3StorageException>(() => S3Path.AssertFolderName(""));
        Assert.Throws<S3StorageException>(() => S3Path.AssertFolderName("foo"));
        Assert.Throws<S3StorageException>(() => S3Path.AssertFolderName("/foo"));
        Assert.Throws<S3StorageException>(() => S3Path.AssertFolderName("foo/bar"));
        Assert.Throws<S3StorageException>(() => S3Path.AssertFolderName("/foo/bar"));

        Assert.DoesNotThrow(() => S3Path.AssertFolderName("/"));
        Assert.DoesNotThrow(() => S3Path.AssertFolderName("foo/"));
        Assert.DoesNotThrow(() => S3Path.AssertFolderName("/foo/"));
        Assert.DoesNotThrow(() => S3Path.AssertFolderName("foo/bar/"));
        Assert.DoesNotThrow(() => S3Path.AssertFolderName("/foo/bar/"));
    }


}