using System;
using NUnit.Framework;

namespace SS14.Launcher.Tests;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class UriHelperTests
{
    [Test]
    [TestCase("server.sandwich14.com", "http://server.sandwich14.com:1212/status")]
    [TestCase("ss14s://server.sandwich14.com", "https://server.sandwich14.com/status")]
    [TestCase("ss14s://server.sandwich14.com:1212", "https://server.sandwich14.com:1212/status")]
    [TestCase("ss14s://server.sandwich14.com/foo", "https://server.sandwich14.com/foo/status")]
    public void GetServerStatusAddress(string input, string expected)
    {
        var uri = UriHelper.GetServerStatusAddress(input);

        Assert.That(uri, Is.EqualTo(new Uri(expected)));
    }
}