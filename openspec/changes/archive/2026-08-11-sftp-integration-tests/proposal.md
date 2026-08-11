## Why

Nothing in `SftpSession` is tested. Every SFTP test in the repository stops at the boundary — `Describe`
and `FormatPermissions` are exercised against a faked `ISftpFile`, and everything above them is faked at
`ISftpSession`. That was a deliberate constraint: no test may require a server, and there was no way to
have one.

The cost has been visible twice in the last two changes. `SftpSession.ConnectAsync` leaked a client and
its authentication on every call after the first, and nothing could have caught it. The fix for that —
release the old client, and unsubscribe `ErrorOccurred` *before* disposing so a dying client cannot
raise `Dropped` against its successor — is exactly the kind of ordering bug a unit test cannot see and a
real connection would. It is currently verified by a human reconnecting a few times and watching.

Testcontainers .NET and `atmoz/sftp` remove the constraint. A container is a server the test owns, so
"no test may require a server" becomes "every test brings its own".

## What Changes

- Add `Testcontainers` to the test project, and a fixture that starts one `atmoz/sftp` container for the
  test run and hands out its host and port.
- Add integration tests for `SftpSession` against that container, under
  `mRemoteNGTests.IntegrationTests.Sftp`, covering what only a real server can answer:
  - connecting with a password, and the diagnostics that come with it;
  - **reconnecting repeatedly without leaking a client or a stale `Dropped` subscription** — the
    regression the last change could only leave to a smoke test;
  - listing a directory, including permission strings, hidden entries and the `.`/`..` filtering;
  - uploading and downloading, with progress reported and cancellation honoured;
  - rename, delete, create directory and create file, and the errors the server gives for each
    (deleting a non-empty directory, listing what is not there, writing where permission is denied);
  - real symbolic links, which is the only place `IsSymbolicLink` and `ResolvesToDirectoryAsync` have
    ever been exercised as anything but a fake's boolean.
- Fail loudly when Docker is unreachable rather than passing quietly, with an opt-out environment
  variable for a developer working without it.

Out of scope, deliberately: the recursive expander and the file manager UI. Those are worth covering the
same way and are a second change; this one establishes the fixture and proves the runner can drive a
Linux container.

## Capabilities

### New Capabilities

- `sftp-integration-testing`: running the SFTP client against a real server in a container — the fixture,
  its lifetime, how it fails when Docker is absent, and what the session is expected to do against it.

### Modified Capabilities

None. This asserts existing behaviour rather than changing it. Anything it finds is a bug to fix, not a
requirement to rewrite.

## Impact

- `Directory.Packages.props` — a `Testcontainers` package version. **Normally off-limits to an issue
  fix**; in scope here because the change is a testing-infrastructure request.
- `mRemoteNGTests/mRemoteNGTests.csproj` — the package reference.
- `mRemoteNGTests/IntegrationTests/Sftp/` — the container fixture and the tests.
- No production code changes. If a test finds a defect, fixing it is a separate change with its own
  reasoning.

Deliberately **not** touched: `run-tests.ps1`, `run-tests-core.sh` and the group definitions. Putting the
tests under `mRemoteNGTests.IntegrationTests.Sftp` places them in the existing Integration group, which
already matches `FullyQualifiedName~mRemoteNGTests.IntegrationTests` and is already excluded from the
Remaining group's filter. The 100% DLL coverage check is satisfied without a new group.

CI runs on a self-hosted `windows-2025-vs2026` runner with Docker Desktop, so the container runs there as
it does locally. The test assembly targets `net10.0-windows` and cannot move to a Linux runner; it drives
a Linux container from Windows, which is what Docker Desktop is for.
