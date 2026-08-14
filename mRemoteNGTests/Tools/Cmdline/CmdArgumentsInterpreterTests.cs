using System.Runtime.Versioning;
using mRemoteNG.Tools.Cmdline;
using NUnit.Framework;

namespace mRemoteNGTests.Tools.Cmdline;

/// <summary>
/// What the argument interpreter makes of each accepted form.
/// </summary>
/// <remarks>
/// <para>
/// Written against the defect first and then inverted, so each assertion is known to be capable of
/// failing. What used to happen: every argument was split on <c>^-{1,2}|^/|=|:</c>, whose alternation
/// is anchored for the prefixes and not for the separators. <c>C:\stores\confCons.xml</c> therefore
/// split at the drive letter and produced a parameter named <c>\stores\confCons.xml</c>, while the
/// switch that was waiting for it was given the string "true". No absolute path could be passed to
/// any switch in the space-separated form, and no <c>host:port</c> either.
/// </para>
/// <para>
/// The forms that already worked are here too. They are the regression surface: a value with no
/// <c>:</c> or <c>=</c> in it parsed correctly before this change, which is exactly why the defect
/// survived — <c>--connect "My Server"</c> works and <c>--cons C:\path</c> does not.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public class CmdArgumentsInterpreterTests
{
    [Test]
    public void SpaceSeparatedAbsolutePath_IsTakenWhole()
    {
        // Was: cons = "true", plus an invented parameter named "\stores\confCons.xml".
        CmdArgumentsInterpreter args = new(["--cons", @"C:\stores\confCons.xml"]);

        Assert.Multiple(() =>
        {
            Assert.That(args["cons"], Is.EqualTo(@"C:\stores\confCons.xml"));
            Assert.That(args[@"\stores\confCons.xml"], Is.Null);
        });
    }

    [Test]
    public void SpaceSeparatedHostAndPort_IsTakenWhole()
    {
        // Was: quickconnect = "true".
        CmdArgumentsInterpreter args = new(["--quickconnect", "server.example.com:2222"]);

        Assert.That(args["quickconnect"], Is.EqualTo("server.example.com:2222"));
    }

    [Test]
    public void SpaceSeparatedValueContainingEquals_IsTakenWhole()
    {
        CmdArgumentsInterpreter args = new(["--connect", "name=value"]);

        Assert.That(args["connect"], Is.EqualTo("name=value"));
    }

    [Test]
    public void SpaceSeparatedValueContainingBothSeparators_IsTakenWhole()
    {
        CmdArgumentsInterpreter args = new(["--cons", @"C:\stores\a=b\confCons.xml"]);

        Assert.That(args["cons"], Is.EqualTo(@"C:\stores\a=b\confCons.xml"));
    }

    [TestCase(@"--cons:C:\stores\confCons.xml")]
    [TestCase(@"--cons=C:\stores\confCons.xml")]
    [TestCase(@"/cons:C:\stores\confCons.xml")]
    [TestCase(@"-cons:C:\stores\confCons.xml")]
    public void InlineValue_IsSplitAtTheFirstSeparatorOnly(string argument)
    {
        CmdArgumentsInterpreter args = new([argument]);

        Assert.That(args["cons"], Is.EqualTo(@"C:\stores\confCons.xml"));
    }

    [TestCase("--cons")]
    [TestCase("-cons")]
    [TestCase("/cons")]
    public void EveryPrefix_TakesTheFollowingArgumentAsItsValue(string prefixedSwitch)
    {
        CmdArgumentsInterpreter args = new([prefixedSwitch, @"C:\stores\confCons.xml"]);

        Assert.That(args["cons"], Is.EqualTo(@"C:\stores\confCons.xml"));
    }

    [Test]
    public void ATrailingSeparatorLeavesTheValueToTheNextArgument()
    {
        // `--cons: <path>` is the form the switches page showed for years, so a separator with
        // nothing after it means "the value is next", not "the value is empty". Reporting an empty
        // inline value here would drop the path as a bare argument, and would stop CommandLineParser
        // expanding environment variables in it before the single-instance forward.
        CmdArgumentsInterpreter args = new(["--cons:", @"C:\stores\confCons.xml"]);

        Assert.That(args["cons"], Is.EqualTo(@"C:\stores\confCons.xml"));
    }

    [Test]
    public void ATrailingSeparatorWithNothingAfterItIsAFlag()
    {
        CmdArgumentsInterpreter args = new(["--cons:"]);

        Assert.That(args["cons"], Is.EqualTo(CmdArgumentsInterpreter.FlagValue));
    }

    [Test]
    public void ASwitchAwaitingAValueIsNotGivenTheNextSwitch()
    {
        CmdArgumentsInterpreter args = new(["--cons:", "--connect", "ConnA"]);

        Assert.Multiple(() =>
        {
            Assert.That(args["cons"], Is.EqualTo(CmdArgumentsInterpreter.FlagValue));
            Assert.That(args["connect"], Is.EqualTo("ConnA"));
        });
    }

    [Test]
    public void ABareFlagIsTrue()
    {
        CmdArgumentsInterpreter args = new(["--exitafter"]);

        Assert.That(args["exitafter"], Is.EqualTo("true"));
    }

    [TestCase("\"My Server\"")]
    [TestCase("'My Server'")]
    public void EnclosingQuotesAreRemovedFromASeparateValue(string quoted)
    {
        CmdArgumentsInterpreter args = new(["--connect", quoted]);

        Assert.That(args["connect"], Is.EqualTo("My Server"));
    }

    [Test]
    public void EnclosingQuotesAreRemovedFromAnInlineValue()
    {
        CmdArgumentsInterpreter args = new(["/param3:\"Test-:-work\""]);

        Assert.That(args["param3"], Is.EqualTo("Test-:-work"));
    }

    [Test]
    public void ASwitchFollowedByAnotherSwitch_IsAFlagAndDoesNotEatIt()
    {
        CmdArgumentsInterpreter args = new(["--exitafter", "--connect", "ConnA"]);

        Assert.Multiple(() =>
        {
            Assert.That(args["exitafter"], Is.EqualTo("true"));
            Assert.That(args["connect"], Is.EqualTo("ConnA"));
        });
    }

    [Test]
    public void ABareArgumentWithNothingWaiting_CreatesNoParameter()
    {
        // Was: a parameter named "\typo.xml" set to "true" — an invented switch indistinguishable
        // from a typo, which is why a mistyped path produced neither an error nor an effect.
        CmdArgumentsInterpreter args = new([@"C:\typo.xml"]);

        Assert.Multiple(() =>
        {
            Assert.That(args[@"\typo.xml"], Is.Null);
            Assert.That(args[@"C:\typo.xml"], Is.Null);
        });
    }

    [Test]
    public void TheExecutablePathIsNotAParameter()
    {
        // Every real invocation starts with this, and it is a bare argument with nothing waiting.
        CmdArgumentsInterpreter args = new([@"C:\Program Files\mRemoteNG\mRemoteNG.exe", "--exitafter"]);

        Assert.That(args["exitafter"], Is.EqualTo("true"));
    }

    [Test]
    public void ANameValuedSwitchIsUnaffected()
    {
        // Worked before the change and must still: a value with no ':' or '=' never split.
        CmdArgumentsInterpreter args = new(["--connect", "My Server"]);

        Assert.That(args["connect"], Is.EqualTo("My Server"));
    }

    [Test]
    public void TheFirstOccurrenceOfASwitchWins()
    {
        CmdArgumentsInterpreter args = new(["--connect", "First", "--connect", "Second"]);

        Assert.That(args["connect"], Is.EqualTo("First"));
    }

    [Test]
    public void ASwitchIsCaseInsensitive()
    {
        CmdArgumentsInterpreter args = new(["--CONS", @"C:\stores\confCons.xml"]);

        Assert.That(args["cons"], Is.EqualTo(@"C:\stores\confCons.xml"));
    }

    [Test]
    public void AnUnknownSwitchIsNotAnError()
    {
        CmdArgumentsInterpreter args = new(["--nosuchswitch", "--connect", "ConnA"]);

        Assert.That(args["connect"], Is.EqualTo("ConnA"));
    }
}
