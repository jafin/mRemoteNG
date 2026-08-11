## Why

SSH credential resolution is currently ~250 lines of inline `if/else` welded into the middle of
`PuttyBase.Connect()` (`mRemoteNG/Connection/Protocol/PuttyBase.cs:686-940`). It is reachable only
by PuTTY-family protocols, which produces three concrete problems today:

1. **No other SSH backend can authenticate the way the terminal does.** `ProtocolOpenSSH`
   (`Connection.Protocol.OpenSSH.cs:127-178`) has no password handling at all, so every credential
   provider mRemoteNG supports — Delinea Secret Server, Clickstudios Passwordstate, 1Password,
   PasswordSafe, Vault/OpenBao, LAPS — is silently unavailable to it. `SecureTransfer`
   (SSH file transfer) has its own unrelated username/password textboxes and supports none of them.
2. **Private keys from credential vaults are written to disk in plaintext.** `PuttyBase.Connect()`
   writes the fetched key to `Path.GetTempFileName()` (line 702-703) and later best-effort zero-fills
   and deletes it after a `Thread.Sleep(500)` (lines 993-1019). There is no SSH agent support, so
   there is no alternative path.
3. **Passphrase-protected keys and hardware (FIDO/sk-*) keys are unsupported** on every non-PuTTY
   code path, because there is no agent integration to delegate signing to.

This change extracts credential resolution into a backend-neutral service and adds SSH agent
support (OpenSSH agent and PuTTY Pageant) as a first-class credential source. It is a prerequisite
for the planned SFTP browser and native terminal work — both need to authenticate identically to the
terminal beside them — but it is independently justified by (2) and (3) above.

## What Changes

- **New** `ISshCredentialResolver` service that resolves a `ConnectionInfo` into a backend-neutral
  `ResolvedSshCredential` (username, optional secret, optional key material, agent identities,
  provider provenance). Consolidates all six external credential providers plus empty-credential
  fallbacks, domain qualification, and default-key discovery into one testable unit.
- **New** backend adapters that translate `ResolvedSshCredential` into what each SSH backend needs:
  PuTTY CLI arguments (`-pw`/`-pwfile`/`-i`), OpenSSH CLI arguments, and SSH.NET
  `AuthenticationMethod[]`. Adapters are the only backend-aware code.
- **New** SSH agent support via `SshNet.Agent` (MIT, `2024.2.0.5`, published 2026-07-21, declares
  `SSH.NET >= 2024.2.0 && < 2026.0.0` — satisfied by the resolved `SSH.NET 2025.1.0`). Covers the
  Windows OpenSSH agent named pipe and PuTTY Pageant (named pipe on 0.77+, WM_COPYDATA fallback).
- **Changed** `PuttyBase.Connect()` delegates to the resolver instead of inlining provider logic.
  Observable behaviour is preserved; this is a refactor with no intended functional change.
- **Changed** when an agent holds a usable identity, key material is used from the agent and the
  plaintext temp-key path is not taken.
- **Removed** the `Renci.SshNet.Async` package reference. It is a **dead reference with no
  consumers** — no source file imports `Renci.SshNet.Async`, yet the assembly is copied into
  `mRemoteNG/bin/x64/Release/Assemblies/` and both test outputs, so it ships and is code-signed for
  no benefit. It targets `netstandard1.3` (pulling `NETStandard.Library 1.6.1` into the restore
  graph) and is compiled against `SSH.NET 2016.1.0` while the project resolves `2025.1.0`; any
  future use of it would compile and then fail at runtime. Its purpose — TAP wrappers over the APM
  pattern — is redundant now that SSH.NET exposes native `UploadFileAsync`, `DownloadFileAsync`,
  `ListDirectoryAsync` and friends. Removal is independent of the `SecureTransfer` work below.

**Non-goals.** This change does not add the SFTP browser, does not replace the PuTTY terminal, does
not add host-key (TOFU) verification, and does not change which protocol backend any existing
connection uses. Those are separate changes that depend on this one.

## Capabilities

### New Capabilities

- `ssh-credential-resolution`: Backend-neutral resolution of an mRemoteNG connection into SSH
  credentials, covering all external credential providers, fallback rules, and the adapter contract
  that each SSH backend consumes.
- `ssh-agent-authentication`: Discovery of and authentication via OpenSSH agent and PuTTY Pageant,
  including supported key types, agent-unavailable fallback, and elimination of the plaintext
  temporary private key file.

### Modified Capabilities

None. `openspec/specs/` is currently empty; there are no existing specs to amend.

## Impact

**New code**
- `mRemoteNG/Security/Ssh/ISshCredentialResolver.cs`, `ResolvedSshCredential.cs`,
  `SshCredentialResolver.cs`
- `mRemoteNG/Security/Ssh/Adapters/` — PuTTY, OpenSSH, SSH.NET adapters
- `mRemoteNG/Security/Ssh/Agent/ISshAgentProvider.cs` + implementation over `SshNet.Agent`
- `mRemoteNGTests/Security/Ssh/` — resolver, adapter, and agent-provider fixtures

**Modified code**
- `mRemoteNG/Connection/Protocol/PuttyBase.cs` — remove inline provider chain (lines ~686-940) and
  the temp-key write/wipe block (lines ~700-708, ~993-1019); call the resolver
- `mRemoteNG/Connection/Protocol/SSH/Connection.Protocol.OpenSSH.cs` — build args from the resolver
- `mRemoteNG/Tools/SecureTransfer.cs` — accept a `ResolvedSshCredential`; move the SFTP branch from
  the APM `BeginUploadFile` pattern to native `UploadFileAsync`. The SCP branch stays synchronous —
  `ScpClient` exposes no async methods in SSH.NET 2025.1.0.

**Dependencies**
- **Add** `SshNet.Agent 2024.2.0.5` (MIT). Pinned to an exact version; sits in the authentication
  path, so it requires source review before adoption and should not float.
- **Remove** `Renci.SshNet.Async 1.4.0` — unreferenced by any source file; removal is a standalone
  cleanup that can land independently of every other task in this change.
- Both require editing `Directory.Packages.props` and `mRemoteNG/mRemoteNG.csproj`.
  `Directory.Packages.props` is listed as off-limits for ordinary issue fixes in `CLAUDE.md` and
  needs explicit maintainer sign-off for this change.

**Verification**
- Package changes trigger a restore, so this needs a full build plus the full test suite per the
  `CLAUDE.md` verification table.

**Risk**
- `SshNet.Agent` is a small project (15 stars; the current version has ~338 downloads against 79.8K
  lifetime, most on `2024.2.0.1`). Mitigations: pin exact version, review the source, consider
  vendoring. Its `< 2026.0.0` ceiling will need attention when SSH.NET ships a 2026 release.
- Their README notes FIDO key private keys are unavailable to SSH.NET (`SshAgentPrivateKey.Key` is
  `null`), which may break SSH.NET code paths that inspect `.Key`. Requires a spike before `sk-*`
  keys can be claimed as supported.
- Refactoring `PuttyBase.Connect()` touches the primary connection path for six protocols (SSH1,
  SSH2, Telnet, Rlogin, RAW, Serial). Behaviour-preservation tests come before the extraction.
