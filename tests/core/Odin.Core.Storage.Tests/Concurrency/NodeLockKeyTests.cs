using System;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Storage.Concurrency;

namespace Odin.Core.Storage.Tests.Concurrency
{
    [TestFixture]
    public class NodeLockKeyTests
    {
        [Test]
        public void ImplicitOperator_ValidKey_ReturnsKeyAsString1()
        {
            // Arrange
            var nodeLockKey = NodeLockKey.Create("key");
            const string expected = "key";

            // Act
            string result = nodeLockKey;

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void Create_EmptyKey_ThrowsArgumentException()
        {
            // Arrange
            var key = string.Empty;

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create(key);
            }, "Create should throw ArgumentException for empty parts array.");
        }

        [Test]
        public void ImplicitOperator_ValidKey_ReturnsKeyAsString2()
        {
            // Arrange
            var nodeLockKey = NodeLockKey.Create("part1", "part2");
            const string expected = "part1:part2";

            // Act
            string result = nodeLockKey;

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void Create_ValidParts_ReturnsNodeLockKeyWithJoinedKey()
        {
            // Arrange
            const string expectedKey = "part1:part2:part3";

            // Act
            var nodeLockKey = NodeLockKey.Create("part1", "part2", "part3");

            // Assert
            Assert.That(nodeLockKey.Key, Is.EqualTo(expectedKey));
        }

        [Test]
        public void Create_SinglePart_ReturnsNodeLockKeyWithSinglePartKey()
        {
            // Arrange
            const string expectedKey = "singlePart";

            // Act
            var nodeLockKey = NodeLockKey.Create(expectedKey);

            // Assert
            Assert.That(nodeLockKey.Key, Is.EqualTo(expectedKey));
        }

        [Test]
        public void Create_NullParts_ThrowsArgumentNullException()
        {
            // Arrange
            string[] parts = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
            {
                // ReSharper disable once ExpressionIsAlwaysNull
                NodeLockKey.Create(parts!);
            }, "Create should throw ArgumentNullException for null parts.");
        }

        [Test]
        public void Create_EmptyPartsArray_ThrowsArgumentException()
        {
            // Arrange
            var parts = Array.Empty<string>();

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create(parts);
            }, "Create should throw ArgumentException for empty parts array.");
        }

        [Test]
        public void Create_PartsWithNullElement_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create("part1", null!, "part3");
            }, "Create should throw ArgumentException for null elements in parts.");
        }

        [Test]
        public void Create_PartsWithEmptyString_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create("part1", "", "part3");
            }, "Create should throw ArgumentException for empty string in parts.");
        }

        [Test]
        public void Create_PartsWithWhitespaceString_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create("part1", "  ", "part3");
            }, "Create should throw ArgumentException for whitespace string in parts.");
        }

        [Test]
        public void Create_LargeNumberOfParts_HandlesCorrectly()
        {
            // Arrange
            var parts = Enumerable.Range(0, 100).Select(i => $"part{i}").ToArray();
            var expectedKey = string.Join(":", parts);

            // Act
            var nodeLockKey = NodeLockKey.Create(parts);

            // Assert
            Assert.That(nodeLockKey.Key, Is.EqualTo(expectedKey));
        }
    }
}