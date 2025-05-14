using System;
using System.Linq;
using NUnit.Framework;
using Odin.Services.Concurrency;

namespace Odin.Services.Tests.Concurrency
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
            var parts = new[] { "part1", "part2" };
            var nodeLockKey = NodeLockKey.Create(parts);
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
            var parts = new[] { "part1", "part2", "part3" };
            const string expectedKey = "part1:part2:part3";

            // Act
            var nodeLockKey = NodeLockKey.Create(parts);

            // Assert
            Assert.That(nodeLockKey.Key, Is.EqualTo(expectedKey));
        }

        [Test]
        public void Create_SinglePart_ReturnsNodeLockKeyWithSinglePartKey()
        {
            // Arrange
            var parts = new[] { "singlePart" };
            const string expectedKey = "singlePart";

            // Act
            var nodeLockKey = NodeLockKey.Create(parts);

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
                NodeLockKey.Create(parts);
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
            // Arrange
            var parts = new[] { "part1", null!, "part3" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create(parts);
            }, "Create should throw ArgumentException for null elements in parts.");
        }

        [Test]
        public void Create_PartsWithEmptyString_ThrowsArgumentException()
        {
            // Arrange
            var parts = new[] { "part1", "", "part3" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create(parts);
            }, "Create should throw ArgumentException for empty string in parts.");
        }

        [Test]
        public void Create_PartsWithWhitespaceString_ThrowsArgumentException()
        {
            // Arrange
            var parts = new[] { "part1", "  ", "part3" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                NodeLockKey.Create(parts);
            }, "Create should throw ArgumentException for whitespace string in parts.");
        }

        [Test]
        public void Key_Property_CannotBeSetExternally()
        {
            // Arrange
            var parts = new[] { "part1" };
            var nodeLockKey = NodeLockKey.Create(parts);

            // Act & Assert
            // This test ensures the Key property is read-only externally.
            // Since it's 'init-only', it can't be set after initialization, which is enforced by the compiler.
            // We can't write a direct test for setting it (as it won't compile), so we verify the value remains unchanged.
            Assert.That(nodeLockKey.Key, Is.EqualTo("part1"));
        }

        [Test]
        public void Create_LargeNumberOfParts_HandlesCorrectly()
        {
            // Arrange
            var parts = Enumerable.Range(0, 100).Select(i => $"part{i}").ToArray();
            string expectedKey = string.Join(":", parts);

            // Act
            var nodeLockKey = NodeLockKey.Create(parts);

            // Assert
            Assert.That(nodeLockKey.Key, Is.EqualTo(expectedKey));
        }
    }
}