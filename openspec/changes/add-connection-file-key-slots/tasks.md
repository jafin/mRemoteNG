# Tasks

Depends on `replace-default-connection-file-key`, which introduces the per-file key and the two
protectors. There is nothing to add slots to before it lands, and its task 5.5 — no machine protector
on a file outside the user profile — is what this change exists to reverse for the shared case.

Do not start before it ships. Landing slots first would produce files whose `KeyProtectorMachine`
holds a list that the build shipped alongside them reads as a single blob.

## 1. Slots in the format

- [ ] 1.1 Make `KeyProtectorMachine` a `|`-separated list of wrapped keys. A single entry is the existing form, so a file written by the preceding change is a one-element list and needs no migration and no version flag on the attribute. **Not a space:** `Convert.FromBase64String` ignores whitespace, so a space-separated list read by a build expecting one blob would sometimes concatenate silently instead of failing, depending on whether the entries carry `=` padding. `|` is outside the base64 alphabet and is not whitespace, so an older build fails the same way every time.
- [ ] 1.2 Unwrap by trying every slot in turn and accepting the first whose key **decrypts the root sentinel** — not merely the first that unwraps. `ProtectedData.Unprotect` succeeding proves the blob was written by this account, not that it belongs to this file: a slot copied from another file the same user owns unwraps perfectly and yields the wrong key, and the store then opens onto contents that do not decrypt with nothing reported. Continue the search past a slot that unwrapped but did not authenticate.
- [ ] 1.3 Remember which slot succeeded for the session, next to the recovery password `replace-default-connection-file-key` task 5.6 already caches, so the search happens once per run rather than once per read.
- [ ] 1.4 Refuse a file whose slot list is empty but whose attribute is present, rather than reading it as "no machine protector". An attribute that is there and says nothing is a file something went wrong with, not a portable one.
- [ ] 1.5 Bound the slot count and refuse an over-long list **before** trying any slot. The slots are tried in turn, so without a bound a file declaring a hundred thousand of them takes minutes to refuse to open.
- [ ] 1.6 Tests: a one-element list is exactly what the preceding change writes, and an absent attribute still means no machine protector; N slots each unwrap the same key; a foreign slot among valid ones does not prevent the valid one being found; a slot that unwraps to a key from a different file is rejected rather than accepted; an empty list is refused; an over-long list is refused without attempting a slot; a multi-slot value read by a single-slot reader fails deterministically rather than concatenating.

## 2. Earning a slot

- [ ] 2.1 When the store was opened by the recovery password and the current user has no slot, add one **on the next save** — never on open. Rewriting a file that was only read is what `replace-default-connection-file-key` decided against, and on a shared file it would make every open a write to a file other people have open.
- [ ] 2.2 Do not add a slot in the portable edition, whatever the format allows. There is no account worth binding to on a machine the build was carried to, and a slot per machine visited grows without bound and serves nobody.
- [ ] 2.3 Reverse `replace-default-connection-file-key` task 5.5 for the shared case: a file outside the user profile may now carry machine protectors. Leave the portable gate alone — that one is about the edition, not the location. Record in the code that the two rules had a single implementation and no longer do.
- [ ] 2.4 Tests: a second account opening a shared file is prompted once, then not again after a save; a read-only member is prompted every time and the file is not written; the portable edition adds no slot.

## 3. Rekey

- [ ] 3.1 Offer a rekey: new file key, new recovery password, contents re-encrypted, every slot dropped and the current user's written. This is the removal operation — deleting a slot is not offered, because it does not remove access from someone who knows the recovery password and has had the file.
- [ ] 3.2 Say in the confirmation what a rekey does and does not do: copies taken before it still open, up to the point they were taken. Every rekey of every system has this property and a user who is not told it will assume otherwise.
- [ ] 3.3 Take a backup before a rekey, through the existing `FileBackupCreator`, and confirm the backup is of the pre-rekey file. A rekey that half-completes on a share is the one path here that can lose a store.
- [ ] 3.4 A rekey in the portable edition writes no slot, like every other portable write. Rekeying is about the key and the password; it is not an occasion to start binding to a machine the build was carried to.
- [ ] 3.5 Tests: after a rekey the old recovery password opens nothing; the new one opens everything; a pre-rekey backup still opens with the old password; the contents survive the round trip unchanged; a portable rekey writes zero slots.

## 4. Concurrency

- [ ] 4.1 Record a key generation in a root attribute, changed only by a rekey, and **refuse a save whose generation differs from the one this session read**. Without it a member who had the file open before a rekey writes the contents back under the old key and the old recovery password on their next save, silently reinstating the access the rekey removed — to a user who never knew a rekey happened. Refusing a save is unpleasant; only one of the two outcomes is recoverable by the person it happens to.
- [ ] 4.2 Leave ordinary saves last-writer-wins. Nothing about them is a security boundary and they are already last-writer-wins today; the generation check is for the rekey path alone.
- [ ] 4.3 Confirm by inspection that a lost slot costs one prompt — a member whose slot was overwritten by another's save supplies the recovery password and rewrites it on their next save. Note that this is repaired *by the member*, not on its own: a member who never saves is never repaired. No locking. Record the reasoning where the slot is written, not only here.
- [ ] 4.4 Confirm a concurrent save cannot produce a file with a valid slot list and contents keyed to a different file key. This is the one race that would be destructive rather than annoying.
- [ ] 4.5 Tests: two protection objects derived from the same file, saved in sequence, leave a file both can still open; a save held from before a rekey is refused and does not restore the old protectors; a save from before an ordinary save is not refused.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 5.3 `openspec validate add-connection-file-key-slots --strict`.
- [ ] 5.4 Manual: two Windows accounts, one connection file outside both profiles. Migrate from the first; open from the second with the recovery password; save; confirm the second opens silently thereafter **and the first still does**. The second half is the point — this change is worthless if a slot serves one member at a time.
- [ ] 5.5 Manual: rekey from one account, confirm the other is prompted for the new password once and then silent, and that a backup taken before the rekey still opens with the old one.
- [ ] 5.6 Manual: open a file written by `replace-default-connection-file-key` with no slots added, and confirm it opens unchanged and gains a slot only when saved.
- [ ] 5.7 Manual: rekey from one account **while the second has the file open**, then save from the second. It must be refused. This is the path where a race stops being an annoyance and becomes a silent un-revocation, and it is worth doing by hand because a test can only simulate the two sessions.
