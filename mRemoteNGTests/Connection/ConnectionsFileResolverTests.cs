using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Properties;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

[TestFixture, NonParallelizable, SupportedOSPlatform("windows")]
public class ConnectionsFileResolverTests
{
    private string _savedPath = string.Empty;
    private string _resolvedPath = string.Empty;
    private string _resolvedFingerprint = string.Empty;
    private bool _forcePicker;

    [SetUp]
    public void Save()
    {
        _savedPath = OptionsConnectionsPage.Default.ConnectionFilePath;
        _resolvedPath = OptionsConnectionsPage.Default.ResolvedConnectionFilePath;
        _resolvedFingerprint = OptionsConnectionsPage.Default.ResolvedCandidatesFingerprint;
        _forcePicker = OptionsConnectionsPage.Default.ForceConnectionsFilePickerOnNextStart;
        OptionsConnectionsPage.Default.ConnectionFilePath = string.Empty;
        OptionsConnectionsPage.Default.ResolvedConnectionFilePath = string.Empty;
        OptionsConnectionsPage.Default.ResolvedCandidatesFingerprint = string.Empty;
        OptionsConnectionsPage.Default.ForceConnectionsFilePickerOnNextStart = false;
    }

    [TearDown]
    public void Restore()
    {
        OptionsConnectionsPage.Default.ConnectionFilePath = _savedPath;
        OptionsConnectionsPage.Default.ResolvedConnectionFilePath = _resolvedPath;
        OptionsConnectionsPage.Default.ResolvedCandidatesFingerprint = _resolvedFingerprint;
        OptionsConnectionsPage.Default.ForceConnectionsFilePickerOnNextStart = _forcePicker;
    }

    private static ConnectionsFileResolver.Candidate Cand(string path, DateTime mtimeUtc, long size = 100, string? label = null)
        => new(path, mtimeUtc, size, label ?? path);

    [Test]
    public void Resolve_NoCandidates_ReturnsNull_AndDoesNotPrompt()
    {
        bool called = false;
        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            Array.Empty<ConnectionsFileResolver.Candidate>(),
            (_, _) => { called = true; return (null, false); });

        Assert.That(result, Is.Null);
        Assert.That(called, Is.False);
    }

    [Test]
    public void Resolve_SingleCandidate_ReturnsIt_WithoutPrompting()
    {
        ConnectionsFileResolver.Candidate only = Cand(@"C:\a\confCons.xml", DateTime.UtcNow);
        bool called = false;

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { only },
            (_, _) => { called = true; return (null, false); });

        Assert.That(result, Is.SameAs(only));
        Assert.That(called, Is.False);
    }

    [Test]
    public void Resolve_SingleCandidate_ForceFlagShowsPickerAndClearsFlag()
    {
        ConnectionsFileResolver.Candidate only = Cand(@"C:\a\confCons.xml", DateTime.UtcNow);
        OptionsConnectionsPage.Default.ForceConnectionsFilePickerOnNextStart = true;
        bool called = false;

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { only },
            (_, suggested) => { called = true; return (suggested, false); });

        Assert.That(called, Is.True, "Force flag must override the 1-candidate fast path.");
        Assert.That(result, Is.SameAs(only));
        Assert.That(OptionsConnectionsPage.Default.ForceConnectionsFilePickerOnNextStart, Is.False,
            "Force flag must be consumed (cleared) after one launch.");
    }

    [Test]
    public void Resolve_MultipleCandidates_PromptsWithNewestPreselected()
    {
        ConnectionsFileResolver.Candidate oldC = Cand(@"C:\old\confCons.xml", DateTime.UtcNow.AddDays(-30));
        ConnectionsFileResolver.Candidate newC = Cand(@"C:\new\confCons.xml", DateTime.UtcNow);
        ConnectionsFileResolver.Candidate? suggestedSeen = null;

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { oldC, newC },
            (list, suggested) =>
            {
                suggestedSeen = suggested;
                return (suggested, false);
            });

        Assert.That(result, Is.SameAs(newC), "Newest should win when user keeps pre-selection.");
        Assert.That(suggestedSeen, Is.SameAs(newC), "Pre-selected candidate should be the newest by mtime.");
    }

    [Test]
    public void Resolve_UserCancels_ReturnsNull()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow.AddDays(-1));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { a, b },
            (_, _) => (null, false));

        Assert.That(result, Is.Null);
        Assert.That(OptionsConnectionsPage.Default.ResolvedConnectionFilePath, Is.Empty,
            "Nothing is persisted when the user cancels.");
    }

    [Test]
    public void Resolve_RememberChoice_PersistsToBothSettings()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow.AddDays(-1));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { a, b },
            (_, _) => (a, true));

        Assert.That(result, Is.SameAs(a));
        Assert.That(OptionsConnectionsPage.Default.ConnectionFilePath, Is.EqualTo(a.Path));
        Assert.That(OptionsConnectionsPage.Default.ResolvedConnectionFilePath, Is.EqualTo(a.Path));
    }

    [Test]
    public void Resolve_SessionOnlyChoice_UpdatesResolvedButNotSaved()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow.AddDays(-1));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { a, b },
            (_, _) => (b, false));

        Assert.That(result, Is.SameAs(b));
        Assert.That(OptionsConnectionsPage.Default.ConnectionFilePath, Is.Empty,
            "Session-only choice does not touch the persistent ConnectionFilePath.");
        Assert.That(OptionsConnectionsPage.Default.ResolvedConnectionFilePath, Is.EqualTo(b.Path),
            "Session-only choice updates the in-memory resolved path.");
    }

    [Test]
    public void Resolve_PriorSavedChoice_HonouredSilently()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow.AddDays(-1));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);

        // Prior remembered choice carries both the path and the fingerprint
        // of the candidate set that was present when the user picked. Same
        // set on next launch -> honour silently (no picker).
        OptionsConnectionsPage.Default.ResolvedConnectionFilePath = a.Path;
        OptionsConnectionsPage.Default.ResolvedCandidatesFingerprint =
            ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { a, b });
        bool called = false;

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { a, b },
            (_, _) => { called = true; return (null, false); });

        Assert.That(result, Is.SameAs(a));
        Assert.That(called, Is.False, "Prior saved choice with matching fingerprint must not show the picker again.");
    }

    [Test]
    public void Resolve_PriorSavedChoice_NotInCandidates_FallsBackToPrompt()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow.AddDays(-1));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);

        OptionsConnectionsPage.Default.ResolvedConnectionFilePath = @"C:\nolongerexists\confCons.xml";
        bool called = false;

        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { a, b },
            (_, _) => { called = true; return (b, false); });

        Assert.That(result, Is.SameAs(b));
        Assert.That(called, Is.True, "When the saved path is gone, the picker must be shown.");
    }

    [Test]
    public void Resolve_RememberedChoice_ReprompsWhenCandidateSetChanges()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow.AddDays(-1));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);
        ConnectionsFileResolver.Candidate c = Cand(@"C:\c\confCons.xml", DateTime.UtcNow.AddHours(-1));

        // Simulate a prior run where the user picked A with "Remember".
        OptionsConnectionsPage.Default.ResolvedConnectionFilePath = a.Path;
        OptionsConnectionsPage.Default.ResolvedCandidatesFingerprint =
            ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { a, b });

        bool called = false;

        // A new candidate C appeared since last run -> fingerprint differs ->
        // re-prompt instead of silently returning the stale remembered A.
        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { a, b, c },
            (_, _) => { called = true; return (c, false); });

        Assert.That(called, Is.True, "New candidate -> fingerprint changes -> prompt must appear.");
        Assert.That(result, Is.SameAs(c));
    }

    [Test]
    public void Resolve_RememberedChoice_StaysSilentWhenCandidateSetUnchanged()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow.AddDays(-1));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);

        OptionsConnectionsPage.Default.ResolvedConnectionFilePath = a.Path;
        OptionsConnectionsPage.Default.ResolvedCandidatesFingerprint =
            ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { a, b });

        bool called = false;
        ConnectionsFileResolver.Candidate? result = ConnectionsFileResolver.Resolve(
            new[] { a, b },
            (_, _) => { called = true; return (null, false); });

        Assert.That(called, Is.False, "Same set + same mtimes -> remembered choice honoured silently.");
        Assert.That(result, Is.SameAs(a));
    }

    [Test]
    public void ComputeCandidatesFingerprint_IsStableForSameInputRegardlessOfOrder()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc));
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc));

        string first  = ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { a, b });
        string second = ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { b, a });

        Assert.That(first, Is.EqualTo(second), "Fingerprint must not depend on enumeration order.");
        Assert.That(first, Is.Not.Empty);
    }

    [Test]
    public void ComputeCandidatesFingerprint_IgnoresMtimeChanges()
    {
        // The app rewrites confCons.xml on autosave / shutdown-save, so the
        // candidate's mtime ticks forward between launches even when the set
        // of known locations has not changed. Hashing mtime would force the
        // picker to reappear on every second launch, defeating the
        // "Remember this choice" checkbox.
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc));
        ConnectionsFileResolver.Candidate aTouched = Cand(@"C:\a\confCons.xml", new DateTime(2026, 4, 1, 10, 0, 1, DateTimeKind.Utc));

        Assert.That(
            ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { a }),
            Is.EqualTo(ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { aTouched })),
            "Fingerprint must be path-set only — mtime churn from regular app saves must not invalidate it.");
    }

    [Test]
    public void ComputeCandidatesFingerprint_ChangesWhenPathAdded()
    {
        ConnectionsFileResolver.Candidate a = Cand(@"C:\a\confCons.xml", DateTime.UtcNow);
        ConnectionsFileResolver.Candidate b = Cand(@"C:\b\confCons.xml", DateTime.UtcNow);

        Assert.That(
            ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { a }),
            Is.Not.EqualTo(ConnectionsFileResolver.ComputeCandidatesFingerprint(new[] { a, b })),
            "Fingerprint must flip when a new location appears in the candidate set.");
    }

    [Test, NonParallelizable]
    public void DiscoverCandidates_FindsSavedPathWhenItPointsToARealFile()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"mrng_disc_{Path.GetRandomFileName()}.xml");
        File.WriteAllText(tmp, "<?xml version=\"1.0\"?><Connections/>");
        string originalSaved = OptionsConnectionsPage.Default.ConnectionFilePath;
        try
        {
            OptionsConnectionsPage.Default.ConnectionFilePath = tmp;

            IReadOnlyList<ConnectionsFileResolver.Candidate> candidates =
                ConnectionsFileResolver.DiscoverCandidates();

            Assert.That(
                candidates.Any(c => string.Equals(c.Path, tmp, StringComparison.OrdinalIgnoreCase)),
                "Saved ConnectionFilePath must be returned as one of the candidates when the file exists.");
        }
        finally
        {
            OptionsConnectionsPage.Default.ConnectionFilePath = originalSaved;
            try { File.Delete(tmp); } catch { /* best-effort cleanup */ }
        }
    }
}