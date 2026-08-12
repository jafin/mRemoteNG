# Tasks

Depends on `replace-default-connection-file-key`, which introduces the per-file key and the two
protectors. There is nothing to add slots to before it lands, and its task 5.5 — no machine protector
on a file outside the user profile — is what this change exists to reverse for the shared case.

Do not start before it ships. Landing slots first would produce files whose `KeyProtectorMachine`
holds a list that the build shipped alongside them reads as a single blob.

## 1. Slots in the format

- [ ] 1.1 Make `KeyProtectorMachine` a space-separated list of wrapped keys. A single entry is the existing form, so a file written by the preceding change is a one-element list and needs no migration and no version flag on the attribute.
- [ ] 1.2 Unwrap by trying every slot in turn and taking the first that succeeds. A failed `ProtectedData.Unprotect` throws quickly and prompts for nothing, so the search is silent.
- [ ] 1.3 Remember which slot succeeded for the session, next to the recovery password `replace-default-connection-file-key` task 5.6 already caches, so the search happens once per run rather than once per read.
- [ ] 1.4 Refuse a file whose slot list is empty but whose attribute is present, rather than reading it as "no machine protector". An attribute that is there and says nothing is a file something went wrong with, not a portable one.
- [ ] 1.5 Tests: a one-element list is exactly what the preceding change writes; N slots each unwrap the same key; a foreign slot among valid ones does not prevent the valid one being found; an empty list is refused.

## 2. Earning a slot

- [ ] 2.1 When the store was opened by the recovery password and the current user has no slot, add one **on the next save** — never on open. Rewriting a file that was only read is what `replace-default-connection-file-key` decided against, and on a shared file it would make every open a write to a file other people have open.
- [ ] 2.2 Do not add a slot in the portable edition, whatever the format allows. There is no account worth binding to on a machine the build was carried to, and a slot per machine visited grows without bound and serves nobody.
- [ ] 2.3 Reverse `replace-default-connection-file-key` task 5.5 for the shared case: a file outside the user profile may now carry machine protectors. Leave the portable gate alone — that one is about the edition, not the location. Record in the code that the two rules had a single implementation and no longer do.
- [ ] 2.4 Tests: a second account opening a shared file is prompted once, then not again after a save; a read-only member is prompted every time and the file is not written; the portable edition adds no slot.

## 3. Rekey

- [ ] 3.1 Offer a rekey: new file key, new recovery password, contents re-encrypted, every slot dropped and the current user's written. This is the removal operation — deleting a slot is not offered, because it does not remove access from someone who knows the recovery password and has had the file.
- [ ] 3.2 Say in the confirmation what a rekey does and does not do: copies taken before it still open, up to the point they were taken. Every rekey of every system has this property and a user who is not told it will assume otherwise.
- [ ] 3.3 Take a backup before a rekey, through the existing `FileBackupCreator`, and confirm the backup is of the pre-rekey file. A rekey that half-completes on a share is the one path here that can lose a store.
- [ ] 3.4 Tests: after a rekey the old recovery password opens nothing; the new one opens everything; a pre-rekey backup still opens with the old password; the contents survive the round trip unchanged.

## 4. Concurrency

- [ ] 4.1 Confirm by inspection that a lost slot is self-healing — a member whose slot was overwritten by another's save is prompted once and rewrites it on their next save. No locking. Record the reasoning where the slot is written, not only here.
- [ ] 4.2 Confirm a concurrent save cannot produce a file with a valid slot list and contents keyed to a different file key. This is the one race that would be destructive rather than annoying.
- [ ] 4.3 Tests: two protection objects derived from the same file, saved in sequence, leave a file both can still open.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 5.3 `openspec validate add-connection-file-key-slots --strict`.
- [ ] 5.4 Manual: two Windows accounts, one connection file outside both profiles. Migrate from the first; open from the second with the recovery password; save; confirm the second opens silently thereafter **and the first still does**. The second half is the point — this change is worthless if a slot serves one member at a time.
- [ ] 5.5 Manual: rekey from one account, confirm the other is prompted for the new password once and then silent, and that a backup taken before the rekey still opens with the old one.
- [ ] 5.6 Manual: open a file written by `replace-default-connection-file-key` with no slots added, and confirm it opens unchanged and gains a slot only when saved.
