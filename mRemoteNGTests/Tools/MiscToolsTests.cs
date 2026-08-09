using System;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Tools;

public class MiscToolsTests
{
    #region GetBooleanValue

    [Test]
    public void GetBooleanValue_BoolTrue_ReturnsTrue()
    {
        Assert.That(MiscTools.GetBooleanValue(true), Is.True);
    }

    [Test]
    public void GetBooleanValue_BoolFalse_ReturnsFalse()
    {
        Assert.That(MiscTools.GetBooleanValue(false), Is.False);
    }

    [Test]
    public void GetBooleanValue_String1_ReturnsTrue()
    {
        Assert.That(MiscTools.GetBooleanValue("1"), Is.True);
    }

    [Test]
    public void GetBooleanValue_String0_ReturnsFalse()
    {
        Assert.That(MiscTools.GetBooleanValue("0"), Is.False);
    }

    [Test]
    public void GetBooleanValue_Sbyte1_ReturnsTrue()
    {
        Assert.That(MiscTools.GetBooleanValue((sbyte)1), Is.True);
    }

    [Test]
    public void GetBooleanValue_Sbyte0_ReturnsFalse()
    {
        Assert.That(MiscTools.GetBooleanValue((sbyte)0), Is.False);
    }

    [Test]
    public void GetBooleanValue_UnsupportedType_ReturnsFalse()
    {
        Assert.That(MiscTools.GetBooleanValue(42), Is.False);
    }

    #endregion

    #region LeadingZero

    [Test]
    public void LeadingZero_SingleDigit_AddsPadding()
    {
        Assert.That(MiscTools.LeadingZero("5"), Is.EqualTo("05"));
    }

    [Test]
    public void LeadingZero_DoubleDigit_NoPadding()
    {
        Assert.That(MiscTools.LeadingZero("12"), Is.EqualTo("12"));
    }

    #endregion

    #region PrepareValueForDB

    [Test]
    public void PrepareValueForDB_EscapesSingleQuotes()
    {
        Assert.That(MiscTools.PrepareValueForDb("O'Reilly"), Is.EqualTo("O''Reilly"));
    }

    [Test]
    public void PrepareValueForDB_NoQuotes_Unchanged()
    {
        Assert.That(MiscTools.PrepareValueForDb("simple"), Is.EqualTo("simple"));
    }

    #endregion

    #region GetExceptionMessageRecursive

    [Test]
    public void GetExceptionMessageRecursive_SingleException_ReturnsMessage()
    {
        var ex = new InvalidOperationException("test error");
        Assert.That(MiscTools.GetExceptionMessageRecursive(ex), Is.EqualTo("test error"));
    }

    [Test]
    public void GetExceptionMessageRecursive_NestedExceptions_JoinsMessages()
    {
        var inner = new InvalidOperationException("inner error");
        var outer = new InvalidOperationException("outer error", inner);
        string result = MiscTools.GetExceptionMessageRecursive(outer);
        Assert.That(result, Does.Contain("outer error"));
        Assert.That(result, Does.Contain("inner error"));
    }

    [Test]
    public void GetExceptionMessageRecursive_ThreeLevels_JoinsAll()
    {
        var innermost = new InvalidOperationException("level3");
        var middle = new InvalidOperationException("level2", innermost);
        var outer = new InvalidOperationException("level1", middle);
        string result = MiscTools.GetExceptionMessageRecursive(outer);
        Assert.That(result, Does.Contain("level1"));
        Assert.That(result, Does.Contain("level2"));
        Assert.That(result, Does.Contain("level3"));
    }

    #endregion
}
