# Design

## Context

`SftpSession` is the one layer nothing runs. Below it, `Describe` and `FormatPermissions` are tested
against a faked `ISftpFile`; above it, everything fakes `ISftpSession` wholesale. The class that opens a
socket and speaks the protocol has no coverage at all, because the repository forbids tests that require
a server and there was no way to have one on demand.

Two facts make this cheap now. The runner is self-hosted `windows-2025-vs2026` with Docker Desktop, so a
Linux container runs in CI as it does locally — which matters because `mRemoteNGTests` targets
`net10.0-windows` and cannot move to a Linux job. And `ResolvedSshCredential` takes a plain secret:

```
new SftpSession(host, port, new ResolvedSshCredential("tester", secret: "password"))
```

so a test needs no credential repository, no vault and no external provider to get a connected session.

## Goals / Non-Goals

**Goals:**

- Cover `SftpSession` against a real server, especially where being wrong is invisible until somebody
  connects to a real host.
- Catch the reconnect leak class of bug automatically, instead of by a human reconnecting and watching.
- Add no infrastructure to the test runner, and no burden on a developer who is not touching SFTP.

**Non-Goals:**

- The recursive expander and the file manager UI. Worth the same treatment; a second change once the
  fixture exists.
- Key-based or agent authentication against the container. Password reaches the code under test by the
  same path; key material is `SshNetAuthAdapter`'s business and is covered there.
- Replacing any existing unit test. These run alongside; the fakes stay, because they cover the branches
  a real server will not produce on demand.

## Decisions

### D1 — The tests live in the existing Integration group, not a new one

Namespacing them `mRemoteNGTests.IntegrationTests.Sftp` puts them in the Integration group by the filter
already in `test-config.json` (`FullyQualifiedName~mRemoteNGTests.IntegrationTests`), and the Remaining
group's filter already excludes that prefix. So the 100% DLL coverage check passes with no group
definition touched.

This matters more than it looks. `run-tests.ps1`, `run-tests-core.sh` and the group definitions are
files an ordinary change may not touch, and a new group would have meant editing all of them plus the
coverage arithmetic. Choosing the namespace to fit the existing filters costs nothing and keeps the
change to test code and one package reference.

One consequence to verify rather than assume: `IntegrationSetUpFixture` is a `[SetUpFixture]` in
`mRemoteNGTests.IntegrationTests`, and NUnit applies a setup fixture to its namespace *and below*. Our
tests will therefore also get its `TestScope`. That is expected to be harmless — it is the same scope
every other integration test runs in — but it is a task to confirm, not a thing to hope for.

### D2 — One container per run, behind a fixture

`atmoz/sftp` starts in a couple of seconds; per-test would multiply that across every case for no
isolation gain, since each test can work in its own directory. A `[SetUpFixture]` for the
`...IntegrationTests.Sftp` namespace starts it once and disposes it after, so the container's lifetime is
the group's lifetime and Testcontainers' Ryuk removes it even if the run is killed.

Isolation comes from each test creating its own subdirectory under the writable mount, not from a fresh
server. A test that needs the server in a particular state builds that state itself.

Container configuration is `atmoz/sftp`'s documented command form — `user:pass:::dir` — exposing port 22,
with a wait strategy on the port *and* the "Server listening on" log line. Port-only is not enough:
the port is bound before `sshd` is ready to authenticate, and the test would race it.

### D3 — Missing Docker fails, and the opt-out is explicit

A test that passes when its subject never ran reports coverage that does not exist, which is worse than
having no test. So an unreachable daemon is a failure with a message naming Docker, not a skip.

CI has Docker; an unreachable daemon there is a broken runner and should look like one. The developer who
genuinely does not have it sets `MRNG_SKIP_SFTP_INTEGRATION=1`, which is off by default and visible in
the run output when on. Choosing to skip and not noticing are different things, and only the first should
be easy.

Deliberately not `[Ignore]`. The repository forbids it for failing tests, and using it for environment
gating would make a skipped group look identical to a suppressed failure.

### D4 — Reconnection is the case that justifies the change

`ConnectAsync` replaced `_client` and `_authentication` without disposing them. Correct exactly once,
which is all it was ever called — until reconnect called it again. The fix has an ordering constraint
that no unit test can see: `ErrorOccurred` must be unsubscribed *before* the old client is disposed,
because disposing a connected client can raise it, and a `Dropped` from the previous connection would
mark the fresh one as dead.

Against a container that is a direct assertion: reconnect several times, subscribe to `Dropped`, and
require that no event arrives naming a connection that has been replaced. Getting the order wrong makes
this fail. Nothing short of a real connection does.

Breaking the connection *at the server* — rather than closing it politely from the client — is what makes
the third scenario real, and the container is what makes that possible at all.

### D5 — Assert the server's errors, not just its successes

Deleting a non-empty directory, listing a path that is not there, writing where permission is denied:
each is a decision the *server* makes, and the session's contract is that it surfaces the refusal rather
than swallowing it. `SftpSession.DeleteAsync` has a comment explaining that a non-empty directory failing
is the intended outcome — a claim about a real server's behaviour that nothing has ever checked.

`atmoz/sftp` gives a read-only path outside the upload mount, so permission-denied is reachable without
contriving anything.

## Risks / Trade-offs

**The suite gains a Docker dependency** → Confined to one group and one namespace. A developer touching
anything else is unaffected, and the opt-out exists for anyone who needs it. CI already has the daemon.

**Container startup adds seconds to the run** → Once per run, not per test, in a group that already runs
in the parallel phase. Against a 140-second suite it is noise.

**Image pull failures look like test failures** → They are, in the sense that the group could not run.
The wait strategy and Testcontainers' own errors name the cause. Pinning the image tag rather than
tracking `latest` is the mitigation, so a tag moving under us is a deliberate update.

**`atmoz/sftp` is a third-party image** → It is a thin wrapper over OpenSSH and widely used. Pinned by
tag, pulled from Docker Hub, used only in tests, and never shipped.

**A container-based test could hang rather than fail** → Testcontainers' wait strategies have timeouts,
and the group runs under the same harness as everything else. A hang shows up as the group timing out,
which the runner already handles.

## Open Questions

- Should the image tag be pinned to a digest rather than a version tag? A tag is enough while this is one
  image used by one group; a digest is the answer if it ever becomes several.
- Should key-based authentication be covered here too? Left out because the password path reaches the
  same code, but `atmoz/sftp` mounts authorized keys readily if the credential resolver work later wants
  an end-to-end case.
