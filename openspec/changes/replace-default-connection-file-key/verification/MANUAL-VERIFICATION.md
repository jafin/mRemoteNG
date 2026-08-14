# Manual verification — tasks 8.4 to 8.9

Everything in §8 that a test cannot do. Each section says what it proves, what to do, and what
counts as a pass, and each ends with a line to paste back.

Tasks 8.1–8.3 are already done in CI and in the session that wrote §7: full build with no new
analyzer warnings, full suite 4117 passed / 0 failed, `openspec validate --strict` valid.

---

## Before anything else

**Work on a copy.** Every scenario below writes to a connection file, and 8.4 deliberately migrates
one irreversibly. Do not point any of this at the file you actually use.

```powershell
# Re-run this block in EVERY new PowerShell window. Variables do not survive one.
$repo  = "D:\Data\_CodeOS\mremoteng-robertpopa22\mRemoteNG"     # your checkout
$check = "$repo\openspec\changes\replace-default-connection-file-key\verification\check-legacy-key.py"
$exe   = "$repo\mRemoteNG\bin\x64\Release\mRemoteNG.exe"

# Fails now rather than later, for the reason in the warning below.
if (-not (Test-Path $check)) { throw "check-legacy-key.py not found - is `$repo right?" }
if (-not (Test-Path $exe))   { throw "mRemoteNG.exe not found - build it first" }

# A scratch store and a scratch settings location, so nothing touches your real profile.
mkdir $env:USERPROFILE\mrng-verify
copy $env:APPDATA\mRemoteNG\confCons.xml $env:USERPROFILE\mrng-verify\confCons.xml

# Launch against them. The colon form is REQUIRED — see the second warning below.
& $exe --cons:$env:USERPROFILE\mrng-verify\confCons.xml --cfg:$env:USERPROFILE\mrng-verify\settings
```

> **If a command reports a `SyntaxError` inside your connection file, `$check` is not set.**
>
> ```
> File "C:\Users\you\mrng-verify\confCons.xml", line 1
>     <?xml version="1.0" encoding="utf-8"?>
> SyntaxError: invalid syntax
> ```
>
> PowerShell expands an unset variable to nothing rather than complaining, so
> `python $check <store>` becomes `python <store>` and Python tries to run your connection file as
> a script. Nothing is wrong with the file or the script — you are in a shell that never ran the
> block above. Re-run it.

> **Do not use the space-separated form with an absolute path.**
> `--cons $env:USERPROFILE\mrng-verify\confCons.xml` fails with *"The connection file could not be found"* even
> when the file is plainly there. `CmdArgumentsInterpreter` splits arguments on
> `^-{1,2}|^/|=|:` and that `:` is not anchored, so a bare `C:\...` argument splits at the drive
> letter and is read as a new parameter rather than as the waiting switch's value — `cons` then
> receives the literal string `true`. `--cons:<path>` and `/cons:<path>` parse correctly.
>
> This is a real defect rather than a documentation quirk: `CommandLineParser.ExpandSwitchValue`
> explicitly supports "the value is in the next argument", so the normalizer accepts a form the
> interpreter cannot parse. It is invisible to `CommandLineParserTests` because that fixture uses
> `%VAR%\confCons.xml`, which contains no colon. Not fixed here — it is nothing to do with the
> per-file key — but it is worth its own change.

> **The scratch store must be inside your user profile, and that is not arbitrary.**
> `MachineProtectorPolicy` writes no machine protector for a store outside it — a file several
> people might share carries one protector, which would serve exactly one of them and cost everyone
> else a prompt they could never remove. Put the scratch file in `C:\` and 8.4 comes back
> `machine=no` and prompts on every open. Both are correct behaviour and neither is what 8.4 is
> measuring. 8.9 uses a path outside the profile deliberately, and is the section that tests it.

**Keep an untouched original.** `copy $env:USERPROFILE\mrng-verify\confCons.xml $env:USERPROFILE\mrng-verify\confCons.pristine.xml`
before you start. Several sections want a classic file to go back to.

**Have the notifications panel open** — View → Notifications. Two of these scenarios turn on a
message that is written there rather than shown in a dialog. `%APPDATA%\mRemoteNG\mRemoteNG.log`
(or `$env:USERPROFILE\mrng-verify\settings\mRemoteNG.log` with the `--cfg` above) has the same content.

### Builds you will need

| For | Command | Lands in |
|---|---|---|
| 8.4–8.7, 8.9 | `pwsh -File build.ps1` | `mRemoteNG\bin\x64\Release\` |
| 8.8 | download the **v1.82.0** release from GitHub | anywhere separate |

> **One build now serves both editions.** The edition is decided by a `portable.flag` file beside the
> executable, not by how the binary was compiled, so 8.7 needs no second build:
>
> ```powershell
> New-Item -ItemType File (Join-Path (Split-Path $exe) portable.flag)   # portable
> Remove-Item (Join-Path (Split-Path $exe) portable.flag)               # installed
> ```
>
> Restart after either — the edition is resolved once per run. Confirm which you have from the
> startup line in `mRemoteNG\bin\x64\Release\mRemoteNG Connection Manager*.log`: it says
> "Portable Edition" or it does not.
>
> This replaced a compile constant that **every** configuration but `Release Installer` defined, and
> `Release Installer` was built by nothing — so every shipped artefact ran as the portable edition
> and no machine protector was ever written for anyone. That is why 8.4 kept returning `machine=no`
> on a store sitting inside the user profile. See `detect-portable-edition-at-runtime`.

---

## 8.4 — A migrated file is no longer readable with the published key

**Proves:** the change did the one thing it exists to do. Everything else in §8 is about not having
broken something on the way.

The check runs `$check` — set above, and living at
`openspec\changes\replace-default-connection-file-key\verification\check-legacy-key.py` in the
checkout. It reimplements the classic format from scratch — PBKDF2 → AES-256-GCM — and shares no
code with mRemoteNG. Asking mRemoteNG whether mRemoteNG still uses `mR3m` would prove very little.

```powershell
pip install cryptography            # once
```

**Step 1 — prove the script works before trusting a failure.**

```powershell
python $check --self-test
```

It opens a real mRemoteNG file from the test resources and must print
`RESULT: the published key mR3m OPENS this file`, listing recovered passwords. A script that cannot
decrypt anything would report the same clean bill of health on a migrated file and mean nothing.
*(This is also the clearest demonstration of CVE-2023-30367 you will get: real passwords out of a
real file, with no secret involved.)*

**Step 2 — confirm your scratch file is currently readable.**

```powershell
python $check $env:USERPROFILE\mrng-verify\confCons.xml
```

Expect `OPEN`. If it says `shut`, your file already has a master password — take it off, or start
from a file without one, or 8.4 proves nothing.

**Step 3 — migrate.** Launch against the scratch store, then **File → Storage Format…**
→ *Harden*. Set a recovery password when asked (write it down — 8.5 and 8.6 need it).

**Step 4 — check again.**

```powershell
python $check $env:USERPROFILE\mrng-verify\confCons.xml
```

**Pass:** `RESULT: the published key mR3m does not open this file`, `StorageFormat: Hardened`, and
`protectors: machine=yes, recovery=yes`.

The line that matters is every `[shut]` where step 2 printed `[OPEN]` — the same stored passwords,
no longer readable by anyone holding the file. `KdfIterations: 0` here is expected and not a
failure: the contents are keyed on the store's own random key, so nothing is derived from a
password and the KDF attributes are inert.

`machine=no` means the store is not under your user profile. Move it and redo from step 2 — see the
location warning above.

**Step 5 — the other half of the task.** Close and relaunch against the same store.

**Pass:** the tree loads, a connection opens with its saved password, and **you were asked for
nothing**. A prompt here would mean the machine protector is not working and every daily user would
meet a password box; report it rather than typing the recovery password past it. (If step 4 said
`machine=no`, you will be prompted and that is the location problem, not this one.)

> Record: 8.4 self-test ___ / before ___ / after ___ / silent reopen ___

---

## 8.5 — A migrated file on a second Windows account

**Proves:** the recovery protector is a real escape route, and the user is told *why* they are being
asked rather than left to conclude their password is wrong.

**Setup:** any second local Windows account. `Settings → Accounts → Other users → Add account →
I don't have this person's sign-in information → Add a user without a Microsoft account` is enough;
it does not need to be an administrator.

1. From the first account, copy the migrated `$env:USERPROFILE\mrng-verify\confCons.xml` somewhere both accounts
   can read — `C:\Users\Public\mrng-verify\` works.
2. Sign in as the second account. Copy the file to a folder that account owns.
3. Run mRemoteNG there — colon form, as above:
   `.\mRemoteNG.exe --cons:<that copy> --cfg:<a scratch settings folder>`

**Pass:**
- You are asked for a password.
- The recovery password opens the file and the connections are intact.
- The notifications panel carries
  **"This connection file was protected by a different Windows account or machine."**

**Judge this one, don't just tick it.** That message is written to the notifications panel, and the
password dialog is modal — so unless the panel was already open and visible, you may well meet the
prompt *first* and the explanation *after*. The design's claim (§3.2) is that the reason reaches the
user "before any prompt", and it is collected before the prompt but not necessarily *seen* before
it. If that is how it plays out, say so: it is a real gap between what the design promises and what
a user experiences, and it becomes its own task rather than a shrug.

> Record: 8.5 prompted ___ / opened ___ / message present ___ / message seen before or after the prompt ___

---

## 8.6 — Restoring a backup somewhere the machine protector cannot help

**Proves:** the scenario that changed the design. A DPAPI-only file would have left every rolling
backup unopenable after a profile rebuild, silently.

**Do this by hand even though tests cover it.** The tests copy files; this is the first time a real
rolling backup written by the real backup path gets restored on a machine that cannot use its
machine protector.

1. Back on the first account, with the migrated store loaded, check Tools → Options → Backup that
   backups are enabled. Default naming is `confCons.xml.20260813-101500xxxx.backup`, written
   **beside the connection file** — `BackupLocation` is not a backup destination in this fork, so
   there is nothing to point elsewhere.
2. Make a handful of edits, saving between each, until several `.backup` files exist next to the
   store.
3. Copy **only the backup files** to the second Windows account (or another machine).
4. There, rename the newest to `confCons.xml` and open it.

**Pass:** the recovery password opens the backup and the connections are intact.

Also worth doing, because it is the failure §6 is built around: put a *damaged* file in place as
`confCons.xml`, keep the backups beside it, and open it.

**Pass:** it recovers from a backup. And in the wrong-password case — dismiss or fail the recovery
prompt — you get **one** error naming the recovery password, not one warning per backup, and the
live file is left exactly as it was.

> Record: 8.6 backups produced ___ / restored elsewhere ___ / one message not N ___ / live file untouched ___

---

## 8.7 — The portable edition, and files exchanged with the installed one

**Proves:** the two editions did not end up writing files the other cannot read. This is the failure
the original draft of the design had, so it is the one worth being fussy about.

```powershell
# Same build as every other section. The marker is what makes it portable.
New-Item -ItemType File (Join-Path (Split-Path $exe) portable.flag)
```

Restart afterwards and confirm the startup log now says "Portable Edition".

For **7b** you need it on a stick, and there `pwsh -File build.ps1 -Portable` still helps: it
produces a self-contained copy in `mRemoteNG\bin\x64\Portable\` that needs no .NET on the target
machine. **Check that folder for a `portable.flag` and create one if it is missing** — `build.ps1`
is off-limits to agents under this repository's rules, so it does not write the marker yet and
currently relies on the transitional compile constant instead. That is task 4.1 of
`detect-portable-edition-at-runtime`.

**7a — portable, with a recovery password.** On machine A, run the portable build against a fresh
store, add a connection, harden it, set a recovery password. Then:

```powershell
python $check <the stick's confCons.xml>
```

**Pass:** `protectors: machine=no, recovery=yes`. A machine protector here would be the bug — it
binds the file to machine A, which is the opposite of what a USB build is for.

**7b — the same stick on machine B.** Run it there.
**Pass:** the recovery password opens it. This is the only step in §8 that genuinely needs a second
physical machine; a second Windows account is not the same test.

**7c — portable, declining.** Fresh store on the stick, File → Storage Format… → **Decline**.
**Pass:** a message appears saying the file is protected by a key published in the application's
source. Read it and judge whether it actually lands — this is new text and 7.2 is the whole reason
it exists. `check-legacy-key.py` should say `OPEN` for that file, which is the point being made.

**7d — installed file opened by portable.** Copy the 8.4 migrated file onto the stick and open it
with the portable build, on either machine.
**Pass:** the recovery password opens it. (The machine protector is present and useless there, which
is exactly the case.)

**7e — portable file opened by installed.** Copy the 7a file to the installed build and open it.
**Pass:** the recovery password opens it. Then save, and re-run the script.
**Pass:** `machine=yes` now — the installed edition adopted the file by wrapping the same key a
second way. Reopen: no prompt.

> Record: 8.7 a ___ b ___ c ___ d ___ e ___

---

## 8.8 — The previous release meeting a migrated file

**Proves:** an older build refuses rather than damaging the file. A build that does not know the
third sentinel must not treat it as "not protected" and write over it.

**The refusal is not the interesting part.** v1.82.0 predates the sentinel, so of course it cannot
decrypt the file and will report *something*. What matters is what it does **next**: it has the same
`TryRecoverFromBackup` walk, without §6's protector-failure clause, so a failed load sends it
through the backup set — and if it finds an older **classic** backup it can read, it restores that
and copies it over the migrated file. The user loses everything since the migration and is told
their file was recovered.

**So the file must be tested with its backups beside it.** A migrated file copied somewhere on its
own never reaches the walk, and would pass this section while the actual defect went unseen. A mixed
set is the normal state: migration leaves the pre-migration rolling backups in place.

Download the **v1.82.0** release from `https://github.com/robertpopa22/mRemoteNG/releases` and unpack
it somewhere separate. It keeps its settings beside its own executable, so it will not disturb
anything.

```powershell
# A copy of the WHOLE folder: the migrated store and every .backup beside it.
$old = "C:\mrng-88"
Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $env:USERPROFILE\mrng-verify $old -Recurse

# Confirm the set is mixed — at least one classic backup is what makes this a real test.
Get-ChildItem $old -Filter *.backup | ForEach-Object {
  $fmt = if (Select-String -Path $_.FullName -Pattern 'StorageFormat="Hardened"' -Quiet) { 'hardened' } else { 'CLASSIC' }
  "{0,-45} {1}" -f $_.Name, $fmt
}

# Fingerprint everything, so any change at all is visible afterwards.
Get-ChildItem $old -File | Get-FileHash | Select-Object Path,Hash | Sort-Object Path | Format-Table -AutoSize
```

Then open it with the old build — colon form, since v1.82.0 has the same argument defect:

```powershell
<path-to-v1.82.0>\mRemoteNG.exe --cons:C:\mrng-88\confCons.xml
```

Let it do whatever it wants, including accepting any recovery offer it makes. Close it, and
fingerprint again:

```powershell
Get-ChildItem $old -File | Get-FileHash | Select-Object Path,Hash | Sort-Object Path | Format-Table -AutoSize
python $check C:\mrng-88\confCons.xml
```

**Pass:**
- `confCons.xml`'s hash is **unchanged**, and it still reports `StorageFormat: Hardened` with both
  protectors.
- The current build still opens it.

**Fail, and this is the outcome to look for:** `confCons.xml`'s hash changed, or the checker now
reports it classic and readable under `mR3m`. That is the older release having restored a
pre-migration backup over it — silently, and with a message saying it recovered your file.

Note honestly what the refusal *looks like* as a secondary observation. A clean message naming a
version is the hope, not a guarantee; an unhelpful parse error that leaves every hash untouched is a
pass with a caveat, and worth writing down as one.

> Record: 8.8 mixed set confirmed ___ / confCons.xml hash unchanged ___ / still hardened ___ / opens in current build ___ / message quality ___

**If it fails, do not treat it as a v1.82.0 bug to be fixed there** — that release is out and cannot
be changed. It becomes a constraint on *this* change: either the migrated file must be
distinguishable to an older build before it reaches the backup walk, or migration must not leave
readable classic backups beside a hardened store. Both are design work, which is why this section is
worth running before shipping rather than after.

---

## 8.9 — Two accounts, one file outside both profiles

**Proves:** the shared-file rule from §5 — a store outside the user profile gets **no** machine
protector, so it does not serve one person and prompt everyone else forever.

A second local account is enough. No share is needed; only a path outside both profiles.

1. As account A, put a classic connection file at `C:\mrng-shared\confCons.xml` and grant both
   accounts access (`icacls C:\mrng-shared /grant Users:(OI)(CI)M`).
2. Open it as A and harden it, setting a recovery password.
3. Check it: `python $check C:\mrng-shared\confCons.xml`

**Pass:** `protectors: machine=no, recovery=yes` — even though A is the account that migrated it and
could have used a machine protector.

4. Also confirm A was **told** why during migration: the explanation before the password prompt
   should include the paragraph about the file being outside the user profile and everyone needing
   the recovery password.
5. Sign in as B and open the same path.

**Pass:**
- B is asked for the recovery password and it opens.
- **No message about a different Windows account** — there is no machine protector to fail, so
  there is nothing to explain. If that message appears, the location rule did not take effect.
- A and B are prompted identically. Reopen as A to confirm A is asked too. A being silent while B is
  prompted is the exact asymmetry §5 exists to prevent.

> Record: 8.9 no machine protector ___ / A warned at migration ___ / B opens ___ / no wrong-account message ___ / A and B prompted alike ___

---

## Paste back

```
8.4 ___
8.5 ___
8.6 ___
8.7 ___
8.8 ___
8.9 ___
notes:
```

Anything that fails, or any message that reads badly, is worth more than a tick — several of these
scenarios exist because a plausible-sounding design turned out to be wrong when someone tried it.
