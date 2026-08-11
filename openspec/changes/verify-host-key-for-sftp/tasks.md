# Tasks

## 1. Decide where the prompt comes from

- [ ] 1.1 `DialogHostKeyVerifier` takes the terminal's WebView so the prompt appears over the tab being looked at. The file manager has no WebView and the transfer window is its own form. Decide whether each consumer owns a verifier bound to its own control, or whether one verifier is owned above them and passed down. Record the decision and why — this is the substance of the change, not plumbing.
- [ ] 1.2 Decide what a consumer with no window does. `NativeSshTerminalSession` already defaults to `DenyUnverifiedHostKeys`; confirm that is the right default here rather than a quieter one, and that nothing reaches it by accident.

## 2. Verify on the SFTP path

- [ ] 2.1 `SftpSession` takes a `HostKeyGate` and verifies through it, defaulting to deny when none is supplied.
- [ ] 2.2 The file manager supplies a gate backed by `FileHostKeyStore`, so a host accepted for a session is already known here.
- [ ] 2.3 Tests: an unknown key is refused when the verifier declines; a stored key connects with no prompt; a changed key is refused. No test may present UI.

## 3. Verify on the transfer path

- [ ] 3.1 `SSHTransferWindow` verifies through the same gate. It predates the file manager and has never verified, so this is a behaviour change for an existing feature — a user transferring to a host they have never opened a session to will now be asked.
- [ ] 3.2 Tests as for 2.3.

## 4. Confirm the cost does not change where it is already zero

- [ ] 4.1 Re-run `add-sftp-browser-panel` task 1.2 against a host whose key is already accepted: session and panel must still produce zero prompts. If the panel prompts for a host the session accepted, the store is not shared and 2.2 is wrong.
- [ ] 4.2 Manual: connect the panel to a host never opened as a session, confirm exactly one prompt and that accepting it is remembered.
- [ ] 4.3 Manual: change the host key (regenerate the fixture container) and confirm the panel refuses on the same terms as the session.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 5.3 `openspec validate verify-host-key-for-sftp --strict`.
