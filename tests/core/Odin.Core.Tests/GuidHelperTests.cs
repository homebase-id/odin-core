using System;
using NUnit.Framework;

namespace Odin.Core.Tests;

public class GuidHelperTests
{
    [Test]
    public void ItShouldReturnTheLastTwoNibbles()
    {
        var guid = Guid.Parse("00000000-0000-0000-0000-0000000000EF");
        var (lastNibble, secondLastNibble) = GuidHelper.GetLastTwoNibbles(guid);
        Assert.That(lastNibble, Is.EqualTo('f'));
        Assert.That(secondLastNibble, Is.EqualTo('e'));
    }
}