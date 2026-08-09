using System;
using System.Reflection;
using NUnit.Framework;

namespace mRemoteNGTests.Installer;

[TestFixture]
public class InstalledWindowsUpdateCheckerTests
{
    private static readonly string[] SingleValidKb = ["KB1234567"];
    private static readonly string[] TwoValidKbs = ["KB1234567", "KB7654321"];
    private static readonly string[] ValidAndInvalidKbs = ["KB1234567", "KB1234'; DROP--", "KB7654321"];
    private static readonly string[] AllInvalidKbs = ["'; DROP TABLE", "OR 1=1--"];
    private static readonly string[] KbsWithNull = ["KB1234567", null, "KB7654321"];
    private static readonly string[] MixedCaseKbs = ["kb1234567", "KB7654321"];
    private static readonly string[] DigitOnlyKb = ["1234567"];
    private static readonly string[] MixedPrefixKbs = ["1234567", "KB7654321", "kb9999999"];

    private CustomActions.InstalledWindowsUpdateChecker _checker;
    private MethodInfo _sanitizeKbIdMethod;
    private MethodInfo _buildWhereClauseMethod;

    [SetUp]
    public void Setup()
    {
        _checker = new CustomActions.InstalledWindowsUpdateChecker();

        // Use reflection to access private methods for testing
        var type = typeof(CustomActions.InstalledWindowsUpdateChecker);
        _sanitizeKbIdMethod = type.GetMethod("SanitizeKbId", BindingFlags.NonPublic | BindingFlags.Static);
        _buildWhereClauseMethod = type.GetMethod("BuildWhereClauseFromKbList", BindingFlags.NonPublic | BindingFlags.Static);
    }

    #region SanitizeKbId Tests

    [Test]
    public void SanitizeKbId_ValidKbWithPrefix_ReturnsUppercased()
    {
        var result = InvokeSanitizeKbId("KB1234567");
        Assert.That(result, Is.EqualTo("KB1234567"));
    }

    [Test]
    public void SanitizeKbId_ValidKbLowercase_ReturnsUppercased()
    {
        var result = InvokeSanitizeKbId("kb1234567");
        Assert.That(result, Is.EqualTo("KB1234567"));
    }

    [Test]
    public void SanitizeKbId_ValidKbMixedCase_ReturnsUppercased()
    {
        var result = InvokeSanitizeKbId("Kb1234567");
        Assert.That(result, Is.EqualTo("KB1234567"));
    }

    [Test]
    public void SanitizeKbId_ValidNumberOnly_ReturnsWithKbPrefix()
    {
        var result = InvokeSanitizeKbId("1234567");
        Assert.That(result, Is.EqualTo("KB1234567"));
    }

    [Test]
    public void SanitizeKbId_WithWhitespace_ReturnsTrimmedAndUppercased()
    {
        var result = InvokeSanitizeKbId("  KB1234567  ");
        Assert.That(result, Is.EqualTo("KB1234567"));
    }

    [Test]
    public void SanitizeKbId_SqlInjectionAttempt_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("KB1234' OR '1'='1");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_WqlInjectionWithSemicolon_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("KB1234; DROP TABLE");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_WithSpecialCharacters_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("KB1234@#$");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_NullInput_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId(null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_EmptyString_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_WhitespaceOnly_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("   ");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_OnlyKbPrefix_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("KB");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_WithDashes_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("KB-1234567");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeKbId_WithUnderscores_ReturnsEmpty()
    {
        var result = InvokeSanitizeKbId("KB_1234567");
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region BuildWhereClauseFromKbList Tests

    [Test]
    public void BuildWhereClause_SingleValidKb_ReturnsCorrectClause()
    {
        var result = InvokeBuildWhereClause(SingleValidKb);
        Assert.That(result, Is.EqualTo("HotFixID='KB1234567'"));
    }

    [Test]
    public void BuildWhereClause_MultipleValidKbs_ReturnsOrClause()
    {
        var result = InvokeBuildWhereClause(TwoValidKbs);
        Assert.That(result, Is.EqualTo("HotFixID='KB1234567' OR HotFixID='KB7654321'"));
    }

    [Test]
    public void BuildWhereClause_InvalidKb_SkipsInvalid()
    {
        var result = InvokeBuildWhereClause(ValidAndInvalidKbs);
        Assert.That(result, Is.EqualTo("HotFixID='KB1234567' OR HotFixID='KB7654321'"));
    }

    [Test]
    public void BuildWhereClause_AllInvalidKbs_ReturnsEmpty()
    {
        var result = InvokeBuildWhereClause(AllInvalidKbs);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildWhereClause_EmptyList_ReturnsEmpty()
    {
        var result = InvokeBuildWhereClause(Array.Empty<string>());
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildWhereClause_NullValues_SkipsNulls()
    {
        var result = InvokeBuildWhereClause(KbsWithNull);
        Assert.That(result, Is.EqualTo("HotFixID='KB1234567' OR HotFixID='KB7654321'"));
    }

    [Test]
    public void BuildWhereClause_MixedCaseKbs_NormalizesToUppercase()
    {
        var result = InvokeBuildWhereClause(MixedCaseKbs);
        Assert.That(result, Is.EqualTo("HotFixID='KB1234567' OR HotFixID='KB7654321'"));
    }

    [Test]
    public void BuildWhereClause_DigitOnlyKb_AddsKbPrefix()
    {
        var result = InvokeBuildWhereClause(DigitOnlyKb);
        Assert.That(result, Is.EqualTo("HotFixID='KB1234567'"));
    }

    [Test]
    public void BuildWhereClause_MixedPrefixedAndDigitOnly_NormalizesAll()
    {
        var result = InvokeBuildWhereClause(MixedPrefixKbs);
        Assert.That(result, Is.EqualTo("HotFixID='KB1234567' OR HotFixID='KB7654321' OR HotFixID='KB9999999'"));
    }

    #endregion

    #region Helper Methods

    private string InvokeSanitizeKbId(string input)
    {
        return (string)_sanitizeKbIdMethod.Invoke(null, new object[] { input });
    }

    private string InvokeBuildWhereClause(string[] kbList)
    {
        return (string)_buildWhereClauseMethod.Invoke(null, new object[] { kbList });
    }

    #endregion
}