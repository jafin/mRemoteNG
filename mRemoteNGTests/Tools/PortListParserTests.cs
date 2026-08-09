using System.Collections.Generic;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Tools;

public class PortListParserTests
{
    [Test]
    public void ListOfPortsIsParsed()
    {
        bool parsed = PortListParser.TryParse("22, 80, 443, 3389", out List<int> ports, out string error);

        int[] expected = [22, 80, 443, 3389];
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(ports, Is.EqualTo(expected));
        });
    }

    [TestCase("22;80 443")]
    [TestCase("  443 ,22,  80 ")]
    public void SemicolonsSpacesAndPaddingAreAccepted(string input)
    {
        bool parsed = PortListParser.TryParse(input, out List<int> ports, out string error);

        int[] expected = [22, 80];
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(ports, Is.SupersetOf(expected), error);
        });
    }

    [Test]
    public void RangeIsExpanded()
    {
        bool parsed = PortListParser.TryParse("8000-8003", out List<int> ports, out _);

        int[] expected = [8000, 8001, 8002, 8003];
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(ports, Is.EqualTo(expected));
        });
    }

    [Test]
    public void MixedListAndRangeIsSortedAndDeduplicated()
    {
        bool parsed = PortListParser.TryParse("443, 22, 80, 8000-8002, 80, 8001", out List<int> ports, out _);

        int[] expected = [22, 80, 443, 8000, 8001, 8002];
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(ports, Is.EqualTo(expected));
        });
    }

    [Test]
    public void ReversedRangeIsNormalised()
    {
        bool parsed = PortListParser.TryParse("8003-8000", out List<int> ports, out _);

        int[] expected = [8000, 8001, 8002, 8003];
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(ports, Is.EqualTo(expected));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("0")]
    [TestCase("65536")]
    [TestCase("-1")]
    [TestCase("http")]
    [TestCase("22, abc")]
    [TestCase("8000-")]
    [TestCase("8000-99999")]
    public void InvalidInputIsRejectedWithAReason(string input)
    {
        bool parsed = PortListParser.TryParse(input, out List<int> ports, out string error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(ports, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        });
    }

    [Test]
    public void AllPortsCoversTheWholeRange()
    {
        List<int> ports = PortListParser.AllPorts();

        Assert.Multiple(() =>
        {
            Assert.That(ports, Has.Count.EqualTo(65535));
            Assert.That(ports[0], Is.EqualTo(PortListParser.MinPort));
            Assert.That(ports[^1], Is.EqualTo(PortListParser.MaxPort));
        });
    }
}