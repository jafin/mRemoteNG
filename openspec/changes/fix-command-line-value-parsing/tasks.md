# Tasks

Independent of the connection-file security changes. It was found while writing
`replace-default-connection-file-key`'s §8 runbook and is nothing to do with it, so it is not
sequenced behind anything.

## 1. Pin the current behaviour first

- [ ] 1.1 Add tests that assert what the parser does **today** for `--cons C:\path\confCons.xml`: the value is `true`, and a parameter named `\path\confCons.xml` is invented. Mark them clearly as pinning a defect.
- [ ] 1.2 Extend `CommandLineParserTests` so its space-separated cases use a path with a drive letter. The existing fixture uses `%TESTVAR%\confCons.xml`, which has no colon, and passes for the same reason a real path fails — the coverage looks complete and tests nothing.
- [ ] 1.3 Confirm the new tests fail against the current parser before any of it is changed. A fix verified only by tests written after it is a fix verified against itself.

## 2. Separate "is this a switch" from "where does the value begin"

- [ ] 2.1 In `CmdArgumentsInterpreter`, decide switch-hood from the argument's prefix (`-`, `--`, `/`) before looking for a separator, rather than from one regex answering both questions.
- [ ] 2.2 Split an inline value at the first `:` or `=` **after the switch name** only. This is what `Split(txt, 3)` achieves by accident today; make it explicit so it no longer depends on a count argument that reads like an optimisation.
- [ ] 2.3 Take a following argument as the pending switch's value whole, with no inspection of its contents.
- [ ] 2.4 Stop promoting a bare argument to a parameter. Discard it when no switch is waiting.
- [ ] 2.5 Keep the quote-stripping behaviour for both inline and separate values.

## 3. Reporting

- [ ] 3.1 Make `StartupArgumentsInterpreter.ParseCustomConnectionPathArg` report the value it was given rather than the placeholder. The current message ends in `not found: true`, where `true` is the parser's invention and the user typed a path.
- [ ] 3.2 Raise that message somewhere the user will see it. It explains a failure met at startup behind a modal dialog, and the message collector is not where anyone is looking at that moment.
- [ ] 3.3 Decide what happens when `--cons` names a file that does not exist. Silently opening the usual store is the current behaviour and the most dangerous part of the defect — the user asked for one file and edits another. Refusing to start, or starting with an explicit message, are both defensible; choosing is the task.

## 4. Tests

- [ ] 4.1 Invert the §1 tests to the corrected behaviour, keeping the description of what used to happen.
- [ ] 4.2 Every form in the spec: `--switch value`, `--switch:value`, `--switch=value`, `/switch:value`, `-switch value`, bare flag, quoted value.
- [ ] 4.3 Values containing `:` (drive letter, `host:port`), `=`, both, and neither.
- [ ] 4.4 A switch followed by another switch: the first is present with no value, the second is not eaten.
- [ ] 4.5 A bare argument with nothing waiting creates no parameter.
- [ ] 4.6 `--connect "My Server"` and the other name-valued switches still behave exactly as before. These work today, so a regression here would be caused entirely by the fix.
- [ ] 4.7 `GetNormalizedArguments` round-trips every form, since it is what the single-instance forward sends to an already-running process.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures.
- [ ] 5.3 `openspec validate fix-command-line-value-parsing --strict`.
- [ ] 5.4 Manual: `mRemoteNG.exe --cons C:\scratch\confCons.xml` opens that file. This is the case the whole change exists for and it has never worked.
- [ ] 5.5 Manual: the same with `--cfg` and `--log`, and with a path containing spaces.
- [ ] 5.6 Manual: start a second instance with `--connect` while one is running, and confirm the forwarded arguments still arrive intact.
- [ ] 5.7 Remove the warning block from `replace-default-connection-file-key/verification/MANUAL-VERIFICATION.md`, which exists only to route users around this defect.
