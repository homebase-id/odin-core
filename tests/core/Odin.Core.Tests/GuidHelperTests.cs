using System;
using NUnit.Framework;

namespace Odin.Core.Tests;

public class GuidHelperTests
{
    [Test]
    public void ItShouldReturnTheLastTwoNibbles()
    {
        var guid = Guid.Parse("00000000-0000-0000-0000-0000000000EF");
        var (highNibble, lowNibble) = GuidHelper.GetLastTwoNibbles(guid);
        Assert.That(highNibble, Is.EqualTo('e'));
        Assert.That(lowNibble, Is.EqualTo('f'));
    }
}