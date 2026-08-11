# Tasks

## 1. Decide where the prompt comes from

- [x] 1.1 `DialogHostKeyVerifier` takes the terminal's WebView so the prompt appears over the tab being looked at. The file manager has no WebView and the transfer window is its own form. Decide whether each consumer owns a verifier bound to its own control, or whether one verifier is owned above them and passed down. Record the decision and why — this is the substance of the change, not plumbing.
- [x] 1.2 Decide what a consumer with no window does. `NativeSshTerminalSession` already defaults to `DenyUnverifiedHostKeys`; confirm that is the right default here rather than a quieter one, and that nothing reaches it by accident. Note that this denies only what it would have had to ask about: the gate consults the store first, so a known endpoint still connects without a verifier that could say yes.
- [x] 1.3 Decide how the store is shared. `HostKeyGate` holds a store *and* a verifier, so "the same gate everywhere" and "a verifier bound to each window" cannot both be true. Settle which object is common — one `FileHostKeyStore` instance passed to each gate, or separate instances over one file — and say why. Two instances over one file is not obviously wrong (the store locks and re-reads on every call) but it is a decision, not an accident.
- [x] 1.4 Decide how concurrent decisions are serialized. `Evaluate` runs find → ask → save with nothing holding the endpoint across the three, so a session and a panel connecting together can raise two prompts for one key and be answered differently. A lock per endpoint tuple inside the gate is the obvious shape; whatever is chosen has to work across gate instances if 1.3 lands on more than one.

## 2. Verify on the SFTP path

- [x] 2.1 `SftpSession` takes a `HostKeyGate` and verifies through it, defaulting to deny when none is supplied.
- [x] 2.2 The file manager supplies a gate backed by `FileHostKeyStore`, so a host accepted for a session is already known here.
- [x] 2.3 Tests: an unknown key is refused when the verifier declines; a stored key connects with no prompt; a changed key is refused. No test may present UI.
- [x] 2.4 Tests for the endpoint identity and for replacement, which nothing covers today: the same host on a different port, and the same host and port with a different key algorithm, are each asked about; accepting a changed key replaces the stored fingerprint, and the fingerprint it replaced is then treated as changed rather than accepted.
- [x] 2.5 Test that two concurrent evaluations of one unknown endpoint produce one prompt and one shared answer, per 1.4. Drive it through the gate with a verifier that blocks until both callers have arrived, so the race is deterministic rather than hoped for.

## 3. Verify on the transfer path

- [x] 3.1 `SSHTransferWindow` verifies through the same gate. It predates the file manager and has never verified, so this is a behaviour change for an existing feature — a user transferring to a host they have never opened a session to will now be asked.
- [x] 3.2 Tests as for 2.3 and 2.4, **for SCP as well as SFTP**. `SecureTransfer.Connect` branches on `Protocol` to `ScpClt.Connect()` or `SftpClt.Connect()`, and a client built down the untested branch would connect with no verification at all. Either cover both branches or route both through one host-key callback and test that — the second is better, since it makes the branch unable to diverge again.

## 4. Confirm the cost does not change where it is already zero

- [x] 4.1 Re-run `add-sftp-browser-panel` task 1.2 against a host whose key is already accepted: session and panel must still produce zero prompts. Connect both to the same port, or the comparison proves nothing — the store keys on host, port and algorithm, so a panel prompting for a host the session accepted on another port is correct behaviour, not a broken store. With the endpoint held equal, a prompt does mean the store is not shared and 2.2 is wrong.
- [x] 4.2 Manual: connect the panel to a host never opened as a session, confirm exactly one prompt and that accepting it is remembered.
- [ ] 4.3 Manual: change the host key (regenerate the fixture container) and confirm the panel refuses on the same terms as the session.

## 5. Verification

- [x] 5.1 Full build; zero new analyzer warnings.
- [x] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [x] 5.3 `openspec validate verify-host-key-for-sftp --strict`.
