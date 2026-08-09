## Why

When an external credential provider returns a private key, `PuttyBase.Connect()` writes it to disk
in plaintext so PuTTY can load it with `-i`:

```csharp
optionalTemporaryPrivateKeyPath = Path.GetTempFileName();
File.WriteAllText(optionalTemporaryPrivateKeyPath, credential.RevealKeyMaterial());
// ... later, in finally:
Thread.Sleep(500);              // hope the child process has read it by now
// best-effort zero-fill, then delete
```

A private key sits unencrypted in the user's temp directory for the life of the connection attempt,
protected only by a fixed sleep and a best-effort wipe that is skipped entirely if the process dies.

`add-ssh-agent-credential-resolver` was expected to remove this, but the premise it relied on does
not hold. Agent identities cannot stand in for a vault-supplied key: a key the vault has just minted
is by definition **not** already in the agent, and an agent holding some unrelated identity is no
reason to withhold the one that would actually authenticate. That change therefore made agent
identities additive and left the temporary file in place, recording the hazard in its
`ssh-agent-authentication` spec and deferring the fix here. See that change's design.md D5.

The hazard is removable by inverting the direction of travel: instead of *reading* keys from the
agent, *write* the vault-supplied key into it.

## What Changes

- **New** key-injection path: parse provider-supplied key material into an SSH.NET
  `IPrivateKeySource` and add it to the agent via
  `SshAgent.AddIdentity(key, lifetime, confirm)`, so the key lives in agent memory with an
  expiry and never reaches disk. PuTTY then picks it up from Pageant exactly as it would any other
  loaded identity.
- **Changed** `PuttyBase.Connect()` prefers injection and falls back to the existing guarded
  temporary file when injection is not possible.
- **New** diagnostics distinguishing the two paths, so a user can tell which one a connection used
  rather than having to infer it.

**Non-goals.** Does not change credential resolution, adapters, or the additive agent semantics
established by `add-ssh-agent-credential-resolver`. Does not remove the temporary-file path — it
remains the fallback.

## Capabilities

### New Capabilities

- `ssh-agent-key-injection`: Injecting provider-supplied private keys into a running SSH agent
  instead of materialising them to disk, including the conditions under which injection is possible,
  key lifetime, and the fallback contract.

### Modified Capabilities

- `ssh-agent-authentication`: the "Provider-supplied key material is written to a guarded temporary
  file" requirement becomes conditional — the guarded file is the fallback rather than the only path.

## Impact

**Prerequisites.** Depends on `add-ssh-agent-credential-resolver` being applied first: it introduces
`ISshAgentProvider`, `ResolvedSshCredential.KeyMaterial`, and the `SshNet.Agent` dependency this
change builds on.

**Affected code**
- `mRemoteNG/Security/Ssh/Agent/` — injection service alongside the existing provider
- `mRemoteNG/Connection/Protocol/PuttyBase.cs` — prefer injection, keep the temp file as fallback
- `mRemoteNGTests/Security/Ssh/` — injection, fallback, and lifetime coverage

**Risks / open questions to resolve during design**
- `PrivateKeyFile` parses OpenSSH/PEM but **not** PuTTY `.ppk`. The format returned by Delinea and
  Passwordstate is not guaranteed, so injection may simply be impossible for some vault
  configurations. The fallback is therefore permanent, not transitional.
- Injection requires a running agent. On Windows, PuTTY consumes **Pageant** specifically, so
  injecting into the Windows OpenSSH agent would not help a PuTTY connection.
- Key lifetime and whether to require per-use confirmation are security trade-offs needing an
  explicit decision: a short lifetime narrows exposure but may expire mid-reconnect.
- A key left in the agent outlives the connection attempt in a way the temp file does not; removal
  on disconnect should be considered rather than relying solely on the lifetime.
- Only Delinea and Passwordstate currently materialise key material at all. 1Password and
  PasswordSafe return a key that is silently discarded — a separate live bug, preserved deliberately
  and pinned by `KeyMaterialIsDiscardedForProvidersThatNeverMaterialisedIt` (see D9 of the
  prerequisite change). Whether this change also fixes that should be decided explicitly.
