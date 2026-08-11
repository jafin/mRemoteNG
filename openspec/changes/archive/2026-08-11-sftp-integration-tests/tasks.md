## 1. Package and fixture

- [x] 1.1 Add `Testcontainers` to `Directory.Packages.props`, pinned to the version that restores, and
      reference it from `mRemoteNGTests.csproj`. This file is normally off-limits; it is in scope because
      the change is a testing-infrastructure request.
- [x] 1.2 Create `mRemoteNGTests/IntegrationTests/Sftp/` and confirm that `IntegrationSetUpFixture` in the
      parent namespace applies to it harmlessly — NUnit runs a `[SetUpFixture]` for its namespace and
      below, so our tests inherit its `TestScope`. Confirm, do not assume.
- [x] 1.3 Add `SftpServerFixture`: a `[SetUpFixture]` for the new namespace starting one `atmoz/sftp`
      container for the run, exposing host, port, username, password and the writable directory.
- [x] 1.4 Configure it with the documented `user:pass:::dir` command form, exposing port 22, and wait on
      **both** the port and the "Server listening on" log line — the port binds before `sshd` will
      authenticate, and waiting on it alone races the server.
- [x] 1.5 Pin the image tag rather than tracking `latest`, so the image moving is a deliberate update.
- [x] 1.6 Fail with a message naming Docker when the daemon is unreachable, and honour
      `MRNG_SKIP_SFTP_INTEGRATION=1` as an explicit, off-by-default opt-out. Do not use `[Ignore]`.
- [x] 1.7 Give each test its own subdirectory under the writable mount, so isolation does not need a
      fresh container per test.

## 2. Connecting

- [x] 2.1 Test connecting with a password: the session reports itself connected and its home directory is
      what the server placed it in.
- [x] 2.2 Test that connecting with a wrong password fails, and that the session does not report itself
      connected afterwards.
- [x] 2.3 Test that operations on a session that was never connected throw
      `SftpSessionNotConnectedException`, against a real instance rather than a fake.

## 3. Reconnection

- [x] 3.1 Test reconnecting a connected session several times in succession, asserting it is usable after
      each — a listing succeeding is the proof.
- [x] 3.2 Subscribe to `Dropped` across those reconnects and assert that a connection killed at the
      server is reported exactly once, and that a session which has just reconnected is not reported
      dropped.

      *Rewritten during implementation.* This task originally ended "This is what fails if
      `ErrorOccurred` is unsubscribed after the dispose instead of before" — which is not true; see
      8.5 for the measurements. The sentence was removed rather than left standing, because a task
      list that describes coverage the tests do not provide is worse than one that describes less.
      The drop-reporting behaviour above is what the test does assert, and nothing covered it before.
- [x] 3.3 Break the connection at the server rather than closing it politely, then reconnect and confirm
      the session lists again.

## 4. Listing

- [x] 4.1 Test a directory holding files, subdirectories and a dot-file: every entry returned with its
      kind, size and permission string, and `IsHidden` set for the dot-file.
- [x] 4.2 Test that `.` and `..` are not among the returned entries.
- [x] 4.3 Test that the permission string matches the mode the server reports, so `FormatPermissions` is
      checked against a real `ISftpFile` rather than a fake.
- [x] 4.4 Test that listing a path that does not exist surfaces the failure.

## 5. Transfers

- [x] 5.1 Test upload then download of the same content, asserting the bytes round-trip.
- [x] 5.2 Test that progress is reported during both directions and reaches the total.
- [x] 5.3 Test that cancelling a transfer part-way stops it.
- [x] 5.4 Test that uploading over an existing file replaces it, which is what the file manager's
      overwrite handling relies on.

## 6. Mutations and refusals

- [x] 6.1 Test rename, delete, create directory and create file, each confirmed by a subsequent listing.
- [x] 6.2 Test that deleting a non-empty directory fails — the behaviour `SftpSession.DeleteAsync` has a
      comment asserting and nothing has ever checked.
- [x] 6.3 Test that writing where permission is denied surfaces the refusal, using a path outside the
      writable mount.
- [x] 6.4 Test `ExistsAsync` for a file, a directory and something absent.

## 7. Links

- [x] 7.1 Create a real symbolic link on the server and test that a listing reports it with
      `IsSymbolicLink` set.
- [x] 7.2 Test `ResolvesToDirectoryAsync` returns true for a link to a directory and false for a link to a
      file — the distinction a listing cannot make, and the basis of the rule that a recursive transfer
      never descends into a link.
- [x] 7.3 Test that a broken link resolves to false rather than throwing.

## 8. Verification

- [x] 8.1 Run the new group directly and confirm the container starts, the tests pass and it is removed
      afterwards.
- [x] 8.2 Run with `MRNG_SKIP_SFTP_INTEGRATION=1` and confirm the group is skipped and the rest of the
      suite is unaffected.
- [x] 8.3 Stop Docker and confirm the group fails with a message naming Docker, rather than passing.
- [x] 8.4 Run the full suite and confirm the Integration group picks the new tests up without any change
      to `run-tests.ps1`, `run-tests-core.sh` or the group definitions — if it does not, stop and report
      rather than editing those files.
- [ ] 8.5 Deliberately re-break the reconnect ordering (unsubscribe after the dispose) and confirm 3.2
      fails, then restore it. A regression test that does not fail on the regression is not one.

      **Carried out, and it does not fail — so this stays unticked.** Inverting the two lines in
      `ReleaseClient` changes no result, because the failure mode the ordering guards against does
      not occur with SSH.NET 2025.1.0. Measured against the container: killing the connection at the
      server raises `ErrorOccurred` immediately, from the message-listener thread, while the old
      client is still the current one (`drops=1`); disposing that client afterwards raises nothing
      (`drops=0`), and disposing a healthy client is quiet too.

      By its own standard the test would not be one, so 3.2 was rewritten to assert what is real and
      was untested — that a genuine drop is reported exactly once, and that a reconnected session is
      not reported dropped. The ordering stays in `ReleaseClient` as insurance against a library
      version that does raise on dispose. design.md D4 has been corrected to match what was measured.
