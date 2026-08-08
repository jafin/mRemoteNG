## Context

mRemoteNG currently has two SSH backends and is likely to gain a third:

| Backend | Entry point | Credential support today |
|---|---|---|
| PuTTY (`putty.exe` / `PuTTYNG.exe`) | `PuttyBase` — powers SSH1, SSH2, Telnet, Rlogin, RAW, Serial | All six providers, inline |
| OpenSSH (`ssh.exe`) | `ProtocolOpenSSH` | Keys only — **no password path exists** |
| SSH.NET (in-process) | `SecureTransfer` only, today | Password textboxes only |

The provider chain lives inside `PuttyBase.Connect()` at lines 686-940. Because it is a local
`if/else` cascade over `InterfaceControl.Info.ExternalCredentialProvider`, interleaved with
`CommandLineArguments` mutation, it cannot be reused. The result is that the same connection
authenticates three different ways depending on which feature the user reaches it through.

Two constraints shape this design:

1. **`ssh.exe` cannot accept a password non-interactively.** There is no `-pw` equivalent. Any
   credential model that assumes "resolve to a password string" is unusable for that backend. This
   rules out returning SSH.NET `AuthenticationMethod[]` (or PuTTY argv) from the resolver.
2. **SSH.NET has no agent support and cannot share a transport.** Verified by reflection over
   `Renci.SshNet.dll 2025.1.0`: no `Agent`/`Pageant` types exist; `ISession`, `IServiceFactory` and
   `BaseClient.Session` are all `internal`; `SftpClient` has no session-accepting constructor;
   `InternalsVisibleTo` is strong-name-locked to the project's own test assemblies. Agent support
   must therefore come from outside the library.

## Goals / Non-Goals

**Goals:**

- One credential resolution path shared by every SSH backend, present and future.
- Backend-neutral output, so adding or removing a backend does not touch provider logic.
- SSH agent (OpenSSH agent and Pageant) as a first-class credential source.
- Eliminate the plaintext temporary private key file when an agent can serve the key.
- Preserve observable behaviour of existing PuTTY connections exactly.

**Non-Goals:**

- SFTP browser UI, transport sharing, or terminal replacement.
- Host-key / TOFU verification. Needed before any SSH.NET connection is user-facing, but a separate
  change with its own storage and UI surface.
- Feature parity between backends. They are deliberately different capability profiles (see D3).
- Changing which backend any existing connection uses.

## Decisions

### D1 — The resolver returns a neutral credential, not backend arguments

```
                     ┌──────────────────────────┐
   ConnectionInfo ──▶ │  ISshCredentialResolver  │ ──▶ ResolvedSshCredential
                     └──────────────────────────┘            │
                                                              │
              ┌───────────────────┬───────────────────────────┤
              ▼                   ▼                           ▼
       PuttyArgsAdapter    OpenSshArgsAdapter          SshNetAuthAdapter
       -pw / -pwfile       argv + agent env            AuthenticationMethod[]
       -i key.ppk          (drops password —           PrivateKeyAuthenticationMethod
                            reports unsupported)       KeyboardInteractiveAuthenticationMethod
```

`ResolvedSshCredential` carries: effective username (post domain-qualification and
empty-credential fallback), an optional secret, optional key material, zero or more agent
identities, and provenance (which provider answered).

*Alternative rejected:* have the resolver emit SSH.NET `AuthenticationMethod[]` directly. Simpler
for the SFTP work, but structurally excludes `ssh.exe` — the backend that has native agent,
`known_hosts`, `ssh_config` and possibly `ControlMaster`. Given constraint (1), this would have to
be undone as soon as the OpenSSH path grows credential support.

*Alternative rejected:* resolve lazily per-backend via a strategy on `ProtocolBase`. Keeps argv
construction close to the process launch, but reintroduces per-backend provider logic — the exact
duplication this change exists to remove.

### D2 — Adapters declare unsupported combinations rather than silently dropping them

`ProtocolOpenSSH` today builds its argument string with no password handling and no diagnostic. A
user with a Delinea-backed connection gets an interactive `ssh.exe` prompt and no explanation.

Each adapter returns an explicit outcome — the translated form, plus any credential component it
could not honour. The caller surfaces a `MessageClass.WarningMsg` naming the provider and the
backend. This is behaviour-preserving in the sense that authentication proceeds the same way; it
adds the missing diagnostic.

`SSH_ASKPASS` + `SSH_ASKPASS_REQUIRE=force` is a possible future route to passwords over `ssh.exe`,
but Win32-OpenSSH support for it is unverified. Out of scope here; noted in Open Questions.

### D3 — Backends stay deliberately asymmetric

They are not interchangeable implementations. SSH.NET becomes the default profile (vault
credentials, in-process events, future SFTP panel and terminal). `ssh.exe` stays the escape hatch
for real OpenSSH semantics (`ssh_config`, `ProxyJump`, GSSAPI, exotic kex, `known_hosts`). Neither
grows a screen-scraping shim to imitate the other.

The resolver makes this asymmetry cheap: capability differences live in adapters, in one place, and
are reported rather than hidden.

The asymmetry is permanent, not provisional. Two verified findings fix it:

- `ssh.exe` cannot accept a password non-interactively (no `-pw` equivalent), so vault-backed
  credentials can never reach it without an askpass helper (see Open Questions).
- `ssh.exe` cannot multiplex on Windows. Verified against `OpenSSH_for_Windows_9.5p1`:
  `ControlMaster` / `ControlPath` are parsed and echoed by `ssh -G` with no warning, but
  `ssh -O check` fails with `getsockname failed: Not a socket` — the multiplexing socket layer is
  absent. Upstream `PowerShell/Win32-OpenSSH#1328` has been open since 2019-01-23 (last updated
  2026-07-14, `0 - Backlog`). The silent acceptance is the hazard: nothing errors, so a naive
  implementation looks correct while every connection re-authenticates.

`ssh.exe`'s remaining advantages are therefore `ssh_config`/`ProxyJump`, GSSAPI, exotic kex, and
`known_hosts`. Agent parity is closed by D4. That is a real but narrow escape hatch, and it is
terminal-only: **SSH.NET owns the future SFTP panel**, because `ssh.exe` can neither share a
transport nor carry the credentials the panel needs.

### D4 — Agent support via `SshNet.Agent`, wrapped behind `ISshAgentProvider`

`SshNet.Agent 2024.2.0.5` (MIT) yields `IPrivateKeySource[]` from `RequestIdentities()`, which feeds
the public `PrivateKeyAuthenticationMethod`. No reflection, no fork. It covers the Windows OpenSSH
agent named pipe and Pageant (named pipe on 0.77+, `WM_COPYDATA` fallback).

It is wrapped behind an mRemoteNG-owned interface so the dependency is swappable and testable
without a live agent. That matters given the project's size and its position in the auth path.

*Alternative rejected:* implement the agent protocol directly. Both wire protocols are simple, but
Pageant's dual transport and the sk-key signature formats are real surface, and the MIT library is
already maintained against current SSH.NET.

*Alternative rejected:* skip agents, keep temp key files. Leaves the plaintext-key-on-disk hazard in
place and leaves passphrase-protected and hardware keys unsupported.

**Source review (task 5.2), completed 2026-08-08 against tag `2024.2.0.5`** (published 2026-07-21,
`target_commitish: main`). 27 `.cs` files excluding tests. Findings:

- *Untrusted input is bounded.* `AgentReader` validates a declared length against the remaining
  stream before allocating, verifies the actual read matched, and throws `EndOfStreamException` on a
  short read. A hostile or buggy agent cannot drive an unbounded allocation, an infinite loop, or an
  out-of-range read. The release notes for this version specifically cite bounded response lengths
  and inner-string-length validation, so this was a deliberate hardening pass.
- *Private keys never leave the agent.* Signing is delegated through `RsaAgentSignature`,
  `AgentSignature` and `SkAgentHostAlgorithm`; the library holds public halves only.
- *The FIDO caveat is confirmed and narrow.* `SshAgentPrivateKey.Key` is set to `null` in exactly
  one place — the `sk-*` constructor — because SSH.NET has no key type for security keys. This
  corroborates the README and is why D4's implementation filters `sk-*` identities until the spike
  at 10.1 resolves.
- *One residual, niche.* The SSH-certificate constructors assign `Key = certificate.Key` without a
  null check. Only reachable when an agent offers a certificate identity; not on the path this
  change uses. Noted, not blocking.

Pinned as an exact version range `[2024.2.0.5]` in `Directory.Packages.props` so it cannot float
onto an unreviewed release.

### D5 — Agent identities are additive, not a precedence winner (REVISED 2026-08-08)

**The original form of this decision was unsound and is superseded.** It read: "order of preference
… agent identity → configured `PrivateKeyPath` → vault-fetched key material → discovered default
key … when an agent offers a usable identity, `PuttyBase` does not write one."

Two things are wrong with that.

**An agent holding *a* key says nothing about whether it holds *this connection's* key.** Suppressing
a vault-supplied key because some unrelated identity sits in the agent would break authentication
outright, and would do so intermittently — depending on what the user happened to have loaded. SSH
authentication does not work by precedence anyway: the client offers candidate public keys in turn
and the server chooses. `PrivateKeyAuthenticationMethod` accepts an array precisely because of this.

So the correct semantics are **additive**: agent identities are offered alongside the configured
key path, provider-supplied key material and discovered default, ordered agent-first. Nothing is
suppressed. This is what is implemented, and it is pinned by
`AgentIdentitiesDoNotSuppressProviderKeyMaterial` and `AgentIdentitiesDoNotSuppressAConfiguredKeyPath`.

**And the temp-file elimination does not follow from it.** There is no "the key source is an agent
identity" state to branch on. Worse, the backend that writes the temp file is PuTTY, and PuTTY never
consults `ISshAgentProvider` at all — `SshCredentialResolutionOptions.ForPutty` sets
`ConsultAgent: false` because PuTTY talks to Pageant natively. The condition would never fire.

There is also a deeper reason it cannot work: the temp file is only ever written when a *vault*
returns a private key, and a vault-supplied key is by definition **not** already in the agent.

**The hazard is still removable, by a different mechanism.** `SshNet.Agent` exposes
`AddIdentity(IPrivateKeySource)` and `AddIdentity(IPrivateKeySource, TimeSpan? lifetime, bool confirm)`.
Vault key material could be parsed into an SSH.NET `PrivateKeyFile` and *injected* into Pageant with
a short lifetime, after which PuTTY picks it up from the agent and nothing touches disk. That is a
new mechanism rather than a skipped write, and it carries its own risks: `PrivateKeyFile` parses
OpenSSH/PEM but not `.ppk`, the vault's key format is not guaranteed, Pageant must be running, and a
fallback to the temp file is still required whenever any of that fails.

**Maintainer decision 2026-08-08: defer.** Filed as its own change,
`add-ssh-agent-key-injection`. The hazard is real but pre-existing; agent injection is a distinct
mechanism whose failure modes (key-format parsing, agent availability, key lifetime, removal on
disconnect) deserve their own design rather than riding along with a refactor that is otherwise
fully characterized. The `ssh-agent-authentication` spec now records the guarded temporary file as
current behaviour and names the deferral, so no requirement claims something unbuilt.

Unchanged from the original: PuTTY talks to Pageant natively and `ssh.exe` talks to the Windows
agent natively, so `ISshAgentProvider` is consumed only by SSH.NET-backed callers.

### D6 — Extraction is guarded by characterization tests written first

`PuttyBase.Connect()` is the connection path for six protocols. Tests that pin today's produced
argument string across the provider matrix land before the code moves, so the refactor is verified
against observed behaviour rather than an assumed reading of the cascade.

### D10 — Cost and backward-compatibility of a per-connection agent setting

Measured against two existing per-connection booleans (`UseCredSsp`, `RDPAlertIdleTimeout`): a new
connection property touches ~19 files — `AbstractConnectionRecord`, `ConnectionInfo`,
`ConnectionInfoInheritance`, `XmlConnectionNodeSerializer26/27/28`, `XmlConnectionsDeserializer`,
CSV serializer and deserializer, SQL `DataTableSerializer` / `DataTableDeserializer` /
`SqlDatabaseMetaDataRetriever`, `Language.Designer.cs`, `Settings.settings` and
`ConnectionInfoPropertyGrid`.

**Backward compatibility is achievable in both directions.** All three readers are name-based and
defaulting, so neither a missing nor an unknown field is fatal:

| Format | Read pattern | Field missing | Field unknown |
|---|---|---|---|
| XML | `GetAttrBool(attrs, name, defaultValue)` | default | ignored |
| SQL | `if (dataRow.Table.Columns.Contains(name))` | skipped | ignored |
| CSV | `headerSet.Contains(name)`, short rows padded | default | ignored |

The single hard breaker is the schema version. `SqlDatabaseVersionVerifier` compares the database
version against `_currentSupportedVersion` (currently `3.5`); an older client meeting a newer
database matches no upgrader, fails the equality check, logs `ErrorBadDatabaseVersion` and returns
`false` — the connection load fails outright rather than degrading. Anyone running a shared SQL
backend with mixed client versions would be locked out.

So the compatible shape is: **add the column without bumping the version.** Use an idempotent
`ADD COLUMN` (the machinery already exists — `SqlMigrationHelper.MakeMssqlColumnAddsIdempotent`;
it merely needs decoupling from `MsSqlVersionUpdate`), add the XML attribute to serializer 28
without touching `confVersion`, and add the CSV column.

Two consequences to accept:

- An older client that *saves* a connection omits the field, so round-tripping through one silently
  reverts the setting to its default. Graceful, but silent — release-notes material.
- The declared schema version stops being a reliable description of the actual schema. That is
  already true in practice, which is why the `Columns.Contains` guard exists, so this is consistent
  with established behaviour rather than a new compromise.

Independently of cost: neither PuTTY nor `ssh.exe` offers a per-connection agent toggle, and an
agent is a user-wide facility, so the granularity is speculative. It is purely additive later at
the same cost.

**Maintainer decision 2026-08-08: global only.** Not taken on risk grounds — the analysis above
shows a per-connection setting *can* ship compatibly — but on design grounds: the control has no
counterpart in the tools mRemoteNG wraps, and nothing has asked for per-host agent selection. The
`ssh-agent-authentication` spec was amended to require a single global setting and to state
explicitly that no per-connection override exists, so the contract matches what is built. The
compatibility analysis is retained above because it applies unchanged to whenever a per-connection
setting is genuinely wanted.

### D9 — The 1Password/PasswordSafe discarded-key bug is preserved, not fixed

Those two provider branches read a private key and then never use it: the argument builder keys off
the temporary file path that only Delinea and Passwordstate write. So configuring a 1Password- or
PasswordSafe-backed SSH key does nothing today, silently.

**Maintainer decision 2026-08-08: preserve.** This change is a refactor plus agent support; making
two providers start honouring keys they have always ignored is a functional change that would ship
unannounced inside it, and it would alter authentication behaviour for existing connections without
those users asking for it. It is pinned by
`KeyMaterialIsDiscardedForProvidersThatNeverMaterialisedIt` so it cannot change by accident.

Consequence for D5: "agent identity takes precedence over provider-supplied key material" is scoped
to key material that is actually usable — Delinea and Passwordstate. For 1Password and PasswordSafe
there is no key material in play to take precedence over, and the precedence chain falls through to
the configured key path or discovery as it does today.

### D7 — Resolution is re-invocable and never cached

Every `Resolve()` call performs a fresh resolution, including a fresh call to the applicable
external provider. This is a correctness requirement, not a preference: Vault/OpenBao SSH-OTP mints
a **single-use** credential, so a cached result would authenticate the first connection and fail
every one after it — including the SFTP browser's connection sitting beside a terminal, and any
retry after a failed attempt.

The cost is that replayable providers (Delinea, Passwordstate, 1Password, PasswordSafe, LAPS, Vault
password engine) are called once per connection *attempt* rather than once per connection: an extra
API round trip and an extra audit entry. That is the correct trade against silently breaking
single-use credentials.

Callers own the returned `ResolvedSshCredential` and dispose it once the attempt has consumed it; a
credential is never retained across attempts.

*Secret lifetime, honestly stated:* secrets are held in `char[]` buffers zeroed on `Dispose`, which
bounds the lifetime of *this type's copy*. The providers return `string`, which is immutable and
cannot be scrubbed, so a copy survives in the managed heap until collected. Closing that gap means
changing the provider signatures and is out of scope. `RevealSecret()` deliberately carries the
same caveat in its XML doc rather than implying the value is protected.

### D8 — Provider selection is tested at the resolver, not through PuTTY's command line

Task 1.2 originally called for characterizing each external provider in situ, "with the provider
interface faked". There is no such interface: all seven are static calls
(`SecretServerInterface.FetchSecretFromServer`, `PasswordstateInterface.FetchSecretFromServer`,
`OnePasswordCli.ReadPassword`, `PasswordSafeCli.ReadPassword`, `VaultOpenbao.ReadOtpSSH` /
`ReadPasswordSSH`, `LAPSHelper.QueryLAPSPassword`).

*Alternative rejected:* add ~7 `protected virtual` seams to `PuttyBase` purely to fake them. Task 4
would delete all seven two steps later — churn that also widens `PuttyBase`'s surface at exactly
the moment the goal is to narrow it.

Chosen instead: the cascade only produces three locals (username, password, private key), and the
characterization tests added in 1.1/1.3 already pin how those three become command-line arguments.
Provider *selection* is therefore covered at 3.7 against the real `ISshCredentialProvider`
abstraction — a strictly better test, since it asserts the behaviour directly rather than inferring
it from PuTTY's argv.

## Risks / Trade-offs

- **Refactoring the primary connection path for six protocols** → characterization tests first (D6);
  extraction commit contains no behaviour change; full build + full suite per `CLAUDE.md`.
- **`SshNet.Agent` is a small dependency in the authentication path** (15 stars; current version
  ~338 downloads) → pin exact version, review source before adoption, keep behind
  `ISshAgentProvider` so vendoring or replacement is a one-file change.
- **`SshNet.Agent` declares `SSH.NET < 2026.0.0`; SSH.NET uses date-based versioning** → a 2026
  release breaks the constraint and blocks SSH.NET upgrades until upstream moves. Accepted; tracked.
- **FIDO/`sk-*` keys expose a `null` `SshAgentPrivateKey.Key`** per the library's README, which may
  fault SSH.NET paths that inspect `.Key` → spike before claiming sk-key support; until then the
  agent provider filters sk identities out and logs that it did.
- **Removing `Renci.SshNet.Async` carries no code risk** — verified unreferenced; no source file
  imports it. The residual risk is only that the restore graph changes shape (it currently pulls
  `NETStandard.Library 1.6.1`) → full build with restore, per `CLAUDE.md`.
- **Migrating `SecureTransfer`'s SFTP branch from APM to `UploadFileAsync`** is a separate risk from
  the package removal, and is the real one: `SecureTransfer` is upload-only with no test coverage
  today → add coverage before the migration, not after. Note the SCP branch cannot follow;
  `ScpClient` exposes no async methods in SSH.NET 2025.1.0, so it keeps its synchronous
  `Upload` + `Uploading` event shape and the two branches stay asymmetric.
- **Resolver now holds secrets from six providers in one type** → `ResolvedSshCredential` is
  disposable and zeroes secret buffers on dispose; it is never logged, and provenance logging emits
  the provider name only.
- **Behaviour change from the new diagnostic (D2)** → users on OpenSSH connections with vault
  credentials will start seeing a warning that was previously silent. This exposes an existing
  broken configuration rather than creating one; call it out in release notes.

## Migration Plan

1. Add characterization tests against current `PuttyBase.Connect()` argument construction.
2. Introduce `ResolvedSshCredential` + `ISshCredentialResolver` with the provider chain moved
   verbatim. No call-site changes yet.
3. Add `PuttyArgsAdapter`; switch `PuttyBase` to resolver + adapter. Characterization tests must
   stay green with no edits.
4. Add `ISshAgentProvider` and the `SshNet.Agent` implementation, behind an off-by-default setting.
5. Wire agent preference (D5); remove the temp-key path when an agent identity is used.
6. Add `OpenSshArgsAdapter`; switch `ProtocolOpenSSH`, including the D2 diagnostic.
7. Add `SshNetAuthAdapter`; switch `SecureTransfer`; drop `Renci.SshNet.Async`.
8. Enable the agent setting by default once the sk-key spike resolves.

Rollback: steps 4-8 are behind the agent setting or per-backend adapters and revert independently.
Steps 1-3 are a pure refactor; reverting restores the inline cascade.

## Open Questions

- Does Win32-OpenSSH support `SSH_ASKPASS` / `SSH_ASKPASS_REQUIRE=force`? If yes, D2's "unsupported"
  outcome for passwords over `ssh.exe` could become a supported path via a small askpass helper.
- ~~Does Win32-OpenSSH support `ControlMaster` / `ControlPath` multiplexing?~~ **Resolved: no.**
  Verified 2026-08-08 against `OpenSSH_for_Windows_9.5p1`. The options parse and are echoed by
  `ssh -G`, but `ssh -O check` returns `getsockname failed: Not a socket`; Win32-OpenSSH lacks the
  Unix-domain-socket support the feature needs. Upstream `PowerShell/Win32-OpenSSH#1328` open since
  2019, `0 - Backlog`. Consequences: the SFTP panel must be SSH.NET-hosted (recorded in D3), and
  single-auth for server-issued interactive MFA is unreachable without a shared subsystem channel on
  an mRemoteNG-owned transport — i.e. it depends on the native-terminal change *and* an upstream
  SSH.NET change to expose `SendSubsystemRequest` on a public type. Neither is in scope here.
- Should the agent setting be global, per-connection, or both? Per-connection matches how
  `ExternalCredentialProvider` already works; global is simpler and matches user expectation of an
  agent. Leaning both, defaulting per-connection to "inherit global".
- ~~Does any provider need re-invocation rather than caching?~~ **Resolved: re-invocable, never
  cached.** See D7.
