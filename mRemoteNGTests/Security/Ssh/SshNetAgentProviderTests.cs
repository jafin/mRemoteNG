using System;
using System.Collections.Generic;
using System.Linq;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Agent;
using NUnit.Framework;

namespace mRemoteNGTests.Security.Ssh;

/// <summary>
/// Covers <c>specs/ssh-agent-authentication/spec.md</c>.
/// No test here requires a running SSH agent or Pageant.
/// </summary>
[TestFixture]
public class SshNetAgentProviderTests
{
    private static readonly SshAgentIdentity Ed25519 = new("alice@laptop", "ssh-ed25519");
    private static readonly SshAgentIdentity Rsa = new("alice@desktop", "ssh-rsa");
    private static readonly SshAgentIdentity Fido = new("alice@yubikey", "sk-ssh-ed25519@openssh.com");

    // ---- identities are surfaced ---------------------------------------------

    [Test]
    public void IdentitiesFromTheOpenSshAgentAreReturned()
    {
        StubAgentProvider provider = new(openSsh: [Ed25519, Rsa]);

        IReadOnlyList<SshAgentIdentity> identities = provider.GetIdentities(SshAgentQuery.Default);

        Assert.That(identities, Is.EqualTo(new[] { Ed25519, Rsa }));
    }

    [Test]
    public void IdentitiesFromPageantAreReturned()
    {
        StubAgentProvider provider = new(pageant: [Ed25519]);

        Assert.That(provider.GetIdentities(SshAgentQuery.Default), Is.EqualTo(new[] { Ed25519 }));
    }

    [Test]
    public void BothTransportsAreConsultedInOrder()
    {
        StubAgentProvider provider = new(openSsh: [Ed25519], pageant: [Rsa]);

        provider.GetIdentities(SshAgentQuery.Default);

        Assert.That(provider.Consulted, Is.EqualTo(new[] { SshAgentKind.OpenSsh, SshAgentKind.Pageant }));
    }

    [Test]
    public void AnIdentityHeldByBothTransportsIsReturnedOnce()
    {
        StubAgentProvider provider = new(openSsh: [Ed25519], pageant: [Ed25519]);

        Assert.That(provider.GetIdentities(SshAgentQuery.Default), Has.Count.EqualTo(1));
    }

    [Test]
    public void OnlyTheRequestedTransportIsConsulted()
    {
        StubAgentProvider provider = new(openSsh: [Ed25519], pageant: [Rsa]);

        IReadOnlyList<SshAgentIdentity> identities =
            provider.GetIdentities(new SshAgentQuery([SshAgentKind.Pageant]));

        Assert.Multiple(() =>
        {
            Assert.That(provider.Consulted, Is.EqualTo(new[] { SshAgentKind.Pageant }));
            Assert.That(identities, Is.EqualTo(new[] { Rsa }));
        });
    }

    // ---- agent unavailability is non-fatal -------------------------------------

    [Test]
    public void NoAgentRunningYieldsAnEmptySetRatherThanThrowing()
    {
        StubAgentProvider provider = new();

        IReadOnlyList<SshAgentIdentity> identities = null!;
        Assert.DoesNotThrow(() => identities = provider.GetIdentities(SshAgentQuery.Default));
        Assert.That(identities, Is.Empty);
    }

    [Test]
    public void AnAgentThatThrowsIsTreatedAsOfferingNothing()
    {
        ThrowingAgentProvider provider = new();

        IReadOnlyList<SshAgentIdentity> identities = null!;
        Assert.DoesNotThrow(() => identities = provider.GetIdentities(SshAgentQuery.Default));
        Assert.That(identities, Is.Empty);
    }

    [Test]
    public void OneFailingTransportDoesNotSuppressTheOther()
    {
        PartiallyFailingAgentProvider provider = new(failing: SshAgentKind.OpenSsh, working: [Rsa]);

        Assert.That(provider.GetIdentities(SshAgentQuery.Default), Is.EqualTo(new[] { Rsa }));
    }

    [Test]
    public void NoTransportsRequestedMeansNoAgentIsContacted()
    {
        StubAgentProvider provider = new(openSsh: [Ed25519]);

        IReadOnlyList<SshAgentIdentity> identities = provider.GetIdentities(new SshAgentQuery([]));

        Assert.Multiple(() =>
        {
            Assert.That(identities, Is.Empty);
            Assert.That(provider.Consulted, Is.Empty);
        });
    }

    // ---- FIDO filtering ----------------------------------------------------------

    [Test]
    public void FidoIdentitiesAreOfferedByDefault()
    {
        // Filtered by default until the sk-key spike (task 10.1) established that SSH.NET cannot
        // observe the null Key the agent library sets for these. See SshNetAuthAdapterTests.
        StubAgentProvider provider = new(openSsh: [Ed25519, Fido]);

        Assert.That(provider.GetIdentities(SshAgentQuery.Default), Is.EqualTo(new[] { Ed25519, Fido }));
    }

    [Test]
    public void FidoIdentitiesCanBeExcludedExplicitly()
    {
        StubAgentProvider provider = new(openSsh: [Ed25519, Fido]);

        IReadOnlyList<SshAgentIdentity> identities = provider.GetIdentities(
            new SshAgentQuery([SshAgentKind.OpenSsh], IncludeHardwareBacked: false));

        Assert.That(identities, Is.EqualTo(new[] { Ed25519 }));
    }

    [Test]
    public void AnAgentHoldingOnlyFidoIdentitiesStillYieldsThem()
    {
        // The case that makes exclusion the wrong default: a user whose only credential is a
        // security key would otherwise be offered nothing at all.
        StubAgentProvider provider = new(openSsh: [Fido]);

        Assert.That(provider.GetIdentities(SshAgentQuery.Default), Has.Count.EqualTo(1));
    }

    [TestCase("sk-ssh-ed25519@openssh.com")]
    [TestCase("sk-ecdsa-sha2-nistp256@openssh.com")]
    public void EverySkAlgorithmPrefixIsTreatedAsHardwareBacked(string algorithm)
    {
        StubAgentProvider provider = new(openSsh: [new SshAgentIdentity("k", algorithm)]);

        Assert.That(
            provider.GetIdentities(new SshAgentQuery([SshAgentKind.OpenSsh], IncludeHardwareBacked: false)),
            Is.Empty);
    }

    [TestCase("ssh-ed25519")]
    [TestCase("ecdsa-sha2-nistp256")]
    [TestCase("ecdsa-sha2-nistp384")]
    [TestCase("ecdsa-sha2-nistp521")]
    [TestCase("ssh-rsa")]
    [TestCase("rsa-sha2-256")]
    [TestCase("rsa-sha2-512")]
    public void SupportedAlgorithmsAreOffered(string algorithm)
    {
        StubAgentProvider provider = new(openSsh: [new SshAgentIdentity("k", algorithm)]);

        Assert.That(provider.GetIdentities(SshAgentQuery.Default), Has.Count.EqualTo(1));
    }

    // ---- large identity sets ---------------------------------------------------------

    [Test]
    public void ALargeIdentitySetIsReturnedInFullRatherThanCapped()
    {
        // Capping would risk discarding the one key that works. The provider warns instead —
        // see WarnIfLikelyToExhaustServerAuthAttempts.
        SshAgentIdentity[] many = Enumerable.Range(0, 9)
            .Select(i => new SshAgentIdentity($"key{i}", "ssh-ed25519"))
            .ToArray();
        StubAgentProvider provider = new(openSsh: many);

        Assert.That(provider.GetIdentities(SshAgentQuery.Default), Has.Count.EqualTo(9));
    }

    // ---- the real provider, without requiring an agent ------------------------------

    [Test]
    public void TheRealProviderLoadsAndReturnsWithoutAnAgentPresent()
    {
        // The only check that exercises the real SshNet.Agent assembly. It is compiled against
        // Renci.SshNet 2024.2.0.1 while this project resolves 2025.1.0; .NET rolls forward, but
        // a binding failure would surface here as FileLoadException/TypeLoadException rather
        // than the caught-and-logged "agent not available" path.
        SshNetAgentProvider provider = new();

        IReadOnlyList<SshAgentIdentity> identities = null!;
        Assert.DoesNotThrow(() => identities = provider.GetIdentities(SshAgentQuery.Default),
            "Agent enumeration must never throw, whether or not an agent is running.");
        Assert.That(identities, Is.Not.Null);
    }

    // ---- stubs ------------------------------------------------------------------------

    private sealed class StubAgentProvider(
        IReadOnlyList<SshAgentIdentity>? openSsh = null,
        IReadOnlyList<SshAgentIdentity>? pageant = null) : SshNetAgentProvider
    {
        public List<SshAgentKind> Consulted { get; } = [];

        protected override IEnumerable<SshAgentIdentity> ReadFrom(SshAgentKind kind)
        {
            Consulted.Add(kind);
            return (kind == SshAgentKind.Pageant ? pageant : openSsh) ?? Enumerable.Empty<SshAgentIdentity>();
        }
    }

    private sealed class ThrowingAgentProvider : SshNetAgentProvider
    {
        protected override IEnumerable<SshAgentIdentity> ReadFrom(SshAgentKind kind) =>
            throw new InvalidOperationException("agent exploded");
    }

    private sealed class PartiallyFailingAgentProvider(
        SshAgentKind failing,
        IReadOnlyList<SshAgentIdentity> working) : SshNetAgentProvider
    {
        protected override IEnumerable<SshAgentIdentity> ReadFrom(SshAgentKind kind) =>
            kind == failing ? throw new InvalidOperationException("agent exploded") : working;
    }
}