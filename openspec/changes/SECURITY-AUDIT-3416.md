# Security audit mRemoteNG#3416 — implementation order

Nine proposals sit under the upstream audit
([mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)) — seven answering findings,
two answering problems the answers create. They are not independent, and **two of them must be split
across releases** — one task each has to be in users' hands a release before the rest of its own
proposal can safely ship.

Read this before starting any of them. Each proposal states its own dependencies; this is the only
place the cross-release constraints are written down.

## Order

### R1 — independent work, and the prerequisites that need lead time

| Item | Why it is here |
|---|---|
| `require-sql-master-password` **§1** — verify the sentinel | Real defect, no dependencies. A wrong password passes the check roughly once in 256 against unauthenticated AES-CBC. Nothing is gained by holding it behind a multi-release migration |
| `encrypt-sql-backend-with-aead` **§1** — refuse a newer database | **Hard constraint.** Must be deployed before any database can be upgraded. Without it an un-upgraded client legacy-decrypts AEAD ciphertext and shows empty passwords on connections that used to work — which reads as data loss, not as a version mismatch |
| `scope-diagnostic-logging` — all | Independent; no format contract |
| `retire-legacy-rijndael-for-settings` — all | Independent; settings are per-assembly, not shared with upstream. Wide but shallow — eleven call sites |

Optionally also `narrow-connection-password-exposure` §1, which is test-only and locks in behaviour
that is already correct.

### R2 — the compatibility gate

`add-storage-format-opt-in`, alone — **except its confirmation and offer, which move to R3.**

Nothing is gated on the level until the first hardening change, so a confirmation warning that
upstream can no longer read the store would be false when R2 ships on its own. The gate lands here;
the switch appears in R3, when there is something behind it. See that change's tasks.md.

Nothing that changes a stored format may land before it. That is the entire purpose of the change:
this fork writes `%APPDATA%\mRemoteNG\confCons.xml`, upstream's own path and filename, and shipping
any hardening ungated would migrate users into a format the application they came from cannot read.

Within it, §1 — the classic export — comes first. The way back cannot arrive a release after the way
in.

### R3 — connection file hardening

1. `harden-connection-file-kdf`
2. `replace-default-connection-file-key`

The credential file is **not** hardened, permanently. It carries no format level, so hardening it
would break upstream mRemoteNG unconditionally rather than on request. Pinned to classic in
`XmlCredentialPasswordEncryptorDecorator` and covered by tests.

In that order. The second depends on the first for the KDF that stretches its recovery password, and
on R2 for the format level. One release is fine provided the order holds.

### R4 — SQL hardening

1. `encrypt-sql-backend-with-aead` §2–6
2. `require-sql-master-password` §2–5

Both touch the SQL options page; land them together. Being a release later than R1 satisfies the
version-refusal lead time.

### R5 — last

`narrow-connection-password-exposure`.

It shortens an exposure window. Everything above decides whether the encryption means anything at
all, and its §1 tests may already have shipped in R1.

### R6 — after the dust settles

`add-connection-file-key-slots`.

Not from the audit. It repairs a regression R3 introduces for teams: a connection file carries one
machine-bound protector, so on a shared file it serves one member and costs every other member a
prompt at every open. `replace-default-connection-file-key` handles that by writing no machine
protector at all for a file outside the user profile — correct, and it leaves the whole team on a
shared password.

Deliberately last, and deliberately not folded into R3. It turns a root attribute into a set on top
of a format change that is already in flight, and it needs the rekey operation to be honest about
revocation.

Nothing breaks by waiting, because both forms R3 writes carry over unchanged: a file with one machine
protector is a one-element slot list, and a file with none — portable, or anything outside the user
profile under R3's task 5.5 — is an absent attribute either way. R6 needs no migration for either.

The one ordering constraint is the obvious direction — **R6 must not precede R3.** A build that
writes a slot list alongside one that reads a single blob produces files its own contemporaries
cannot open.

## Hard constraints

These are the ones that cause damage if ignored, rather than merely rework:

1. **`encrypt-sql-backend-with-aead` §1 ships a release before §2–6.** A shared database upgraded
   while other clients cannot refuse it produces plausible garbage from unauthenticated decryption,
   not an error.
2. **`add-storage-format-opt-in` ships before every hardening change.** Landing one first is exactly
   the lock-in it exists to prevent.
3. **`add-storage-format-opt-in` §1 — the classic export — precedes its own confirmation.** A user
   who upgrades and wants out must not find the way back unimplemented.
4. **`replace-default-connection-file-key` follows `harden-connection-file-kdf`.** It needs the KDF
   to stretch the recovery password.

## Known wrinkle

`add-storage-format-opt-in` task 2.2 encodes the SQL format level as `ConfVersion`, describing it as
something `encrypt-sql-backend-with-aead` "already gates on" — but that gating is built in R4, after
R2.

It is not circular in practice: R2 defines the level abstraction and reserves `ConfVersion` as the
SQL encoding, R4 realises it. An implementer working R2 will find the SQL half declared but not yet
consumed, and should leave it that way rather than inventing a second marker.

## What each proposal answers

| Proposal | Audit finding | Verdict against this fork |
|---|---|---|
| `add-storage-format-opt-in` | — | Not from the audit. Needed because the fork shares storage with upstream |
| `harden-connection-file-kdf` | M-1 | Partly applies — the iteration count was fixed in v1.80.0; the PBKDF2 PRF is still HMAC-SHA1 |
| `encrypt-sql-backend-with-aead` | M-2 | Applies as written |
| `replace-default-connection-file-key` | H-1 | Applies — CVE-2023-30367, `mR3m` still the default key |
| `require-sql-master-password` | — | Carved out of H-1. A per-user key cannot serve a shared store |
| `retire-legacy-rijndael-for-settings` | L-1 | Applies, and wider than reported — live write paths, not legacy reads |
| `scope-diagnostic-logging` | L-2 | The cited file is gone; the substance partly survives |
| `narrow-connection-password-exposure` | H-2 | Mostly already fixed here; the audit's evidence is against upstream's model |
| `add-connection-file-key-slots` | — | Not from the audit. Repairs what H-1's fix costs a team sharing one connection file |

Two findings needed no proposal. **H-3**, the RDP `NoAuth` default, is already `WarnOnFailedAuth`.
The iteration half of **M-1** — the 1000/10000 counts and the constructor-versus-settings mismatch —
went in v1.80.0.
