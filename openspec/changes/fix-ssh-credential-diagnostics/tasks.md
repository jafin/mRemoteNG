# Tasks

## 1. Provenance

- [x] 1.1 Add a key-path provenance flag to `ResolvedSshCredential` (configured / discovered / none), defaulting to configured so existing constructions keep today's behaviour.
- [x] 1.2 Set it in `SshCredentialResolver` where the path is assigned — `connectionInfo.PrivateKeyPath` is configured, `_keyLocator.Locate(...)` at `SshCredentialResolver.cs:172` is discovered.
- [x] 1.3 Tests: a configured path reports configured; a discovered path reports discovered; no path reports neither.

## 2. Reporting

- [x] 2.1 In `SshNetAuthAdapter.CollectKeyFile`, choose severity from provenance: discovered → `Information`, configured → `Error` (unchanged).
- [x] 2.2 Give discovered keys their own wording. Leave the configured messages exactly as they are, so existing translations and the case that matters are untouched.
- [x] 2.3 Tests: a discovered passphrase-protected key is information and is not called configured; a missing configured key is still an error; a usable discovered key reports nothing.

## 3. Missing username

- [x] 3.1 In `SshNetAuthAdapter.Translate`, report an empty or whitespace username as a diagnostic and build no methods from it, instead of letting `AuthenticationMethod`'s constructor throw.
- [x] 3.2 Tests: translation with an empty username returns rather than throws, carries the diagnostic, and offers no methods.

## 4. Verification

- [x] 4.1 Full build; zero new analyzer warnings.
- [x] 4.2 Full test suite; zero failures, no `[Ignore]`. — 7382 passed.
- [x] 4.3 `openspec validate fix-ssh-credential-diagnostics --strict`.
- [ ] 4.4 Manual, one per shared caller, since all of them replay these diagnostics: a native SSH terminal connection with no key configured reports nothing at error severity; an SFTP connection is unaffected; a file transfer is unaffected.
- [ ] 4.5 Manual: a connection with a **configured** key that is missing or unloadable still reports an error — the regression this change is most likely to cause.
