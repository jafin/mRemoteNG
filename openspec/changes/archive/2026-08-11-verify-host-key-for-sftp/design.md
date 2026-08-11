## Context

Three parts of the application open their own SSH connection: the native terminal session, the file
manager panel, and the file transfer window. Only the first verifies the host key. The machinery to
do it already exists — `HostKeyGate`, `IHostKeyStore`, `FileHostKeyStore`, `DialogHostKeyVerifier` —
and is used from exactly one place:

```csharp
HostKeyGate hostKeys = new(new FileHostKeyStore(), new DialogHostKeyVerifier(_webView));
```

`ProtocolNativeSsh.cs:258`

That line is the whole difficulty. It constructs a store *and* a verifier bound to the terminal's
WebView, so the object that knows what the user has trusted is welded to the window the question was
asked in. The other two consumers need the first half and cannot use the second.

## Goals / Non-Goals

**Goals:**

- Every SSH connection the application opens consults the same trust record.
- One unknown endpoint costs one prompt, however many connections are waiting on it.
- A consumer that cannot ask refuses what it would have had to ask about, and only that.

**Non-Goals:**

- Reading OpenSSH `known_hosts`. Still additive, still a separate change.
- Sharing one SSH transport between consumers. Unavailable; see `add-sftp-browser-panel` design D1.
- Reworking the transfer window beyond moving its connect onto the thread it already starts for the
  upload. That move is in scope — see Risks — because verifying host keys there is what makes
  connecting on the UI thread unsafe rather than merely rude.

## Decisions

### D1 — The store is shared; the verifier is per consumer (tasks 1.1, 1.3)

Each consumer builds its own `HostKeyGate` from **one process-wide `FileHostKeyStore` instance**,
exposed as `SharedHostKeyStore.Instance`, and its own `DialogHostKeyVerifier`.

`HostKeyGate` holds both a store and a verifier, so "one gate everywhere" and "a verifier per window"
are not both available. Of the two, the store is the one that must be common: it is the trust record,
and a record that differs between features is not a record. The verifier is the opposite — it has to
reach a live UI thread, and which thread that is depends on who is connecting.

Worth being precise about what the verifier's `Control` actually does, because the comment at the
call site oversells it. `DialogHostKeyVerifier.Ask` calls `MessageBox.Show` with **no owner**, so the
dialog is screen-centred wherever it comes from. The control is a marshalling target, not a parent
window: it selects the thread the dialog is shown on. That is why the file manager can bind to the
`ConnectionWindow` that hosts its tab and the transfer window to itself, without either needing a
control equivalent to the terminal's WebView.

*Alternative considered — one shared gate above all three consumers.* Rejected: it forces one
verifier, so the file manager's prompt would marshal to the terminal's WebView, and a file manager
opened with no session tab would have no live control to marshal to at all.

*Alternative considered — separate `FileHostKeyStore` instances over one file.* Not wrong: the store
locks and re-reads on every call, so two instances over one path stay consistent. Rejected anyway,
because the per-instance `Lock` then guards nothing another instance respects, and the concurrency
work in D3 would have to reach outside the store to be correct. One instance keeps the invariant
where it can be seen.

### D2 — A consumer that cannot prompt refuses (task 1.2)

`DenyUnverifiedHostKeys` stays the default when no gate is supplied, for `SftpSession` and
`SecureTransfer` as it already is for `NativeSshTerminalSession`.

This denies less than it sounds. `Evaluate` consults the store first and returns `true` on a match
without ever reaching the verifier, so a defaulted consumer still connects to every endpoint the user
has accepted. What it refuses is exactly the set of connections that would have raised a question it
had no way to ask. The quieter alternative — accept when there is nobody to ask — is the silent
acceptance the spec forbids, and it would be invisible by construction.

Nothing reaches the default by accident: both real call sites (`FileManagerLauncher`,
`SSHTransferWindow`) pass a gate explicitly. The default exists for tests and for any future caller
that forgets, and forgetting must cost a refusal rather than a hole.

### D3 — Decisions about one endpoint are serialized process-wide (task 1.4)

`HostKeyGate` takes a `HostKeyDecisionLock`, defaulting to `HostKeyDecisionLock.Shared`. `Evaluate`
becomes:

1. Consult the store. A match returns `true` with no lock taken and no verifier called.
2. Otherwise acquire the endpoint's lock, **re-read the store**, and return `true` if the first
   caller through has since accepted this fingerprint.
3. Otherwise ask, save on acceptance, release.

Step 2 is what makes two concurrent connections cost one prompt: the loser of the race finds the
winner's answer already recorded and takes it. The lock is per endpoint tuple — host, port and
algorithm, the same key the store uses — so a prompt for one host does not block a connection to
another. It lives outside the gate rather than inside it precisely because D1 means several gates
exist; a field on the gate would serialize nothing across them.

The lock table is refcounted and entries are removed on release, so it does not accumulate one
semaphore per endpoint ever contacted.

Step 1 is not merely an optimisation. It keeps the overwhelmingly common case — a known host —
entirely clear of a lock that some other thread may be holding across a modal dialog, which is what
keeps the risk below bounded and rare.

## Risks / Trade-offs

**A blocked UI thread while another consumer's prompt is open** → `SSHTransferWindow` used to call
`SecureTransfer.Connect()` synchronously on the UI thread, which meant the UI thread could enter
`Evaluate`. Waiting there on an endpoint lock held by a background connection whose verifier was
marshalling *to* that same UI thread would have deadlocked both: a thread blocked on a lock is not
pumping messages, so the dialog it was being asked to show could never appear.

Removed at the source. `Connect()` now runs on the thread the window already starts for the upload,
so no UI thread enters the gate and the host key prompt is marshalled to a thread that is free to
show it. Two consequences follow from the window no longer freezing, and both are handled: the
transfer button is disabled *before* the thread starts, since a responsive window would otherwise
accept a second click and abandon the first transfer; and it is re-enabled in a `finally` that
tolerates the window having been closed, because an `ObjectDisposedException` on a background thread
ends the process rather than the transfer.

Two defences are kept rather than relied on. `Evaluate` answers a known key before touching the lock,
so the common case never waits at all, and the wait itself is bounded at 2 minutes — on expiry the
caller re-reads the store and, finding nothing, asks its own question. The degraded outcome is two
prompts, which is what this change set out to avoid but is not a safety failure. A future caller that
connects on the UI thread again gets a slow window, not a dead application.

**A behaviour change for the transfer window** → It has never verified. A user transferring to a host
they have never opened a session to will now be asked, and if the window cannot ask, refused. This is
the point of the change rather than a side effect, but it is a visible change to an old feature and
should be in the release notes.

**One store instance, one file, several processes** → Two mRemoteNG instances still write the same
file through different `FileHostKeyStore` objects, and the last writer wins. Unchanged by this work,
and the cost of losing an entry is being asked again.
