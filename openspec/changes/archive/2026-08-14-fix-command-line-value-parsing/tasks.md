# Tasks

Independent of the connection-file security changes. It was found while writing
`replace-default-connection-file-key`'s §8 runbook and is nothing to do with it, so it is not
sequenced behind anything.

## 1. Pin the current behaviour first

- [x] 1.1 Add tests that assert what the parser does **today** for `--cons C:\path\confCons.xml`: the value is `true`, and a parameter named `\path\confCons.xml` is invented. Mark them clearly as pinning a defect. — Five of them, in a `CmdArgumentsInterpreterTests` headed PHASE 1, including the `host:port` case and the bare-argument promotion.
- [x] 1.2 Extend `CommandLineParserTests` so its space-separated cases use a path with a drive letter. The existing fixture uses `%TESTVAR%\confCons.xml`, which has no colon, and passes for the same reason a real path fails — the coverage looks complete and tests nothing. — `ApplySwitches_SetsConnectionFilePath_WhenConsIsAnAbsolutePathAsASeparateArgument`, alongside the env-var one rather than replacing it.
- [x] 1.3 Confirm the new tests fail against the current parser before any of it is changed. A fix verified only by tests written after it is a fix verified against itself. — Run against the unmodified parser: **9 passed, 1 failed**. The five defect assertions all passed, which is the defect reproducing exactly as the proposal describes it, and the drive-letter test failed. Only then was anything changed.

## 2. Separate "is this a switch" from "where does the value begin"

- [x] 2.1 In `CmdArgumentsInterpreter`, decide switch-hood from the argument's prefix (`-`, `--`, `/`) before looking for a separator, rather than from one regex answering both questions. — Moved to `CommandLineSwitch.TryParse`, **shared with `CommandLineParser`** rather than duplicated. That class already asked the two questions in the right order and got the right answer; leaving two implementations of one rule is what let them disagree in the first place, so `CommandLineParser.TryGetNamedSwitch`/`ParseSwitch` are deleted and both callers now read the same code.
- [x] 2.2 Split an inline value at the first `:` or `=` **after the switch name** only. This is what `Split(txt, 3)` achieves by accident today; make it explicit so it no longer depends on a count argument that reads like an optimisation. — `IndexOfAny(['=', ':'])` on the text after the prefix. A separator at index 0 (`--=value`) names nothing and is treated as a value rather than as a switch called `""`, which is what the old parser invented.
- [x] 2.3 Take a following argument as the pending switch's value whole, with no inspection of its contents.
- [x] 2.4 Stop promoting a bare argument to a parameter. Discard it when no switch is waiting.
- [x] 2.5 Keep the quote-stripping behaviour for both inline and separate values. — The same regex, hoisted to a static field; changing it to a matched-pair check would have altered what a one-sided quote does, which is not this change's business.

## 3. Reporting

- [x] 3.1 Make `StartupArgumentsInterpreter.ParseCustomConnectionPathArg` report the value it was given rather than the placeholder. The current message ends in `not found: true`, where `true` is the parser's invention and the user typed a path. — Falls out of §2: the message already interpolated the value, and the value is now the path. Pinned by a test asserting the message names the path.
- [x] 3.2 Raise that message somewhere the user will see it. It explains a failure met at startup behind a modal dialog, and the message collector is not where anyone is looking at that moment. — `onlyLog: false`, and 3.3 puts the path in the dialog itself.
- [x] 3.3 Decide what happens when `--cons` names a file that does not exist. — **Decided with the user: honour the path.** `CustomConnectionFile` is set to what they typed even when nothing is there, so loading fails against *that* file and the existing "connection file not found" dialog — create new, choose another, import, exit — is raised for it. The dialog now carries the path in its content line, which it never did; every button acts on a file, and "create new" against an unnamed file is a question nobody can answer. Refusing to start was the alternative and was rejected as too harsh for a path that is briefly unreachable, such as a share. What is *not* acceptable, and what this removes, is opening a different store in silence.
  - `CommandLineParser.ApplyConnectionFileOverride` deliberately keeps requiring the file to exist. It writes `ConnectionFilePath` into **settings**, so honouring a missing path there would persist a typo into the user's configuration; the session-only interpreter is the right place for "use this file, it may be new".

## 4. Tests

- [x] 4.1 Invert the §1 tests to the corrected behaviour, keeping the description of what used to happen. — In the fixture's remarks and against each inverted assertion, since "was: cons = true" is what tells the next reader why the test exists.
- [x] 4.2 Every form in the spec: `--switch value`, `--switch:value`, `--switch=value`, `/switch:value`, `-switch value`, bare flag, quoted value.
- [x] 4.3 Values containing `:` (drive letter, `host:port`), `=`, both, and neither.
- [x] 4.4 A switch followed by another switch: the first is present with no value, the second is not eaten.
- [x] 4.5 A bare argument with nothing waiting creates no parameter. — Including the executable path itself, which every real invocation passes as `args[0]`.
- [x] 4.6 `--connect "My Server"` and the other name-valued switches still behave exactly as before. These work today, so a regression here would be caused entirely by the fix. — Also first-occurrence-wins and case-insensitivity, which are unchanged behaviours worth holding.
- [x] 4.7 `GetNormalizedArguments` round-trips every form, since it is what the single-instance forward sends to an already-running process. — Asserted as equality with the input for a line carrying all five forms and absolute paths.
- [x] 4.8 `StartupArgumentsInterpreter`: `--cons <absolute path>` sets the custom file, and a path that does not exist is kept rather than dropped. The second is 3.3's decision, and it is the assertion that stops the silent-substitution behaviour coming back.

## 5. Verification

- [x] 5.1 Full build; zero new analyzer warnings.
- [x] 5.2 Full test suite; zero failures. — **4171 passed, 0 failed** in `mRemoteNGTests.dll`, which is everything `run-tests.ps1` covers; `test-config.json` is updated to match. (`CHANGELOG.md` and `CLAUDE.md` both still say 6,329, which no run has produced since the runner's negated filter clauses were fixed on 2026-08-12 — see the note in `test-config.json`. Out of scope here, and left alone rather than half-corrected.)
- [x] 5.9 Review findings from PR #41 (Copilot, CodeRabbit). Both flagged the same code point: a trailing separator, `--cons:`, reports no inline value, and both proposed reporting an empty one instead. **Not taken** — `--cons: <path>` is the form the switches page showed for years, and an empty inline value drops that path as a bare argument and stops `CommandLineParser` expanding environment variables in it before the single-instance forward. The reasoning is now a comment, and three tests hold it: the value comes from the next argument, a trailing separator alone is a flag, and a following *switch* is not eaten.
  - What the finding did surface is real and one layer up: `--cons` with no path at all carries the flag placeholder, and §3.3 would have honoured `"true"` as a filename — a "connection file not found" dialog about a file called `true`. Nothing was asked for in that case, so the switch is ignored and the message says it needs a path. `CmdArgumentsInterpreter.FlagValue` exists so the two can be told apart by name rather than by a literal.
  - Documentation findings taken: a language on the fenced block (MD040), "Temporary disables" → "Temporarily disables" (inherited from the old page), and aliases written as `— also /c` rather than listed as though each were a separate flag-only switch.
- [x] 5.3 `openspec validate fix-command-line-value-parsing --strict`.
- [x] 5.4 Manual: `mRemoteNG.exe --cons C:\scratch\confCons.xml` opens that file. This is the case the whole change exists for and it has never worked. — Confirmed by the reporter.
- [x] 5.5 Manual: the same with `--cfg` and `--log`, and with a path containing spaces. — Confirmed by the reporter.
- [x] 5.6 Manual: start a second instance with `--connect` while one is running, and confirm the forwarded arguments still arrive intact. — Confirmed by the reporter. The forwarded form was the one part of this that could have broken without any test noticing, because both processes have to agree and only one of them is the one you are looking at.
- [x] 5.7 Remove the warning block from `replace-default-connection-file-key/verification/MANUAL-VERIFICATION.md`, which exists only to route users around this defect. — Replaced rather than deleted: the runbook still uses the colon form throughout, so a reader needs to know that is a leftover and not a requirement. The commands are left as written because they are also correct.
- [x] 5.8 Document it. `docs-website/docs/command-line-switches.md` had no syntax section at all — no mention of which prefixes and separators are accepted, and no entry for `--connect`, `--startup`, `--quickconnect`, `--protocol`, `--exitafter`, `--cfg` or `--log`, which is part of why a form that never worked went unnoticed for so long.
