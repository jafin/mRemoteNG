## Why

A switch value containing a colon cannot be passed as a separate argument. On Windows that means
**no absolute path can be passed to any switch in the space-separated form**:

```
mRemoteNG.exe --cons C:\mrng-verify\confCons.xml
```

`CmdArgumentsInterpreter` splits every argument on `^-{1,2}|^/|=|:` (`CmdArgumentsInterpreter.cs:32`).
The alternation is anchored for `-` and `/` and **not for `:` or `=`**, so the argument
`C:\mrng-verify\confCons.xml` splits at the drive letter into `["C", "\mrng-verify\confCons.xml"]`.
Two parts is the interpreter's "found just a parameter" case, so the value is read as *a new
parameter named `\mrng-verify\confCons.xml`*, and the `cons` that was waiting for it is given the
literal string `"true"`.

Every switch is affected, because the defect is in the value rather than the switch:

| Passed | `args["cons"]` | |
|---|---|---|
| `--cons:C:\path\confCons.xml` | `C:\path\confCons.xml` | correct |
| `/cons:C:\path\confCons.xml` | `C:\path\confCons.xml` | correct |
| `--cons C:\path\confCons.xml` | `true` | **wrong** |
| `--quickconnect host:2222` | `true` | **wrong** |
| `--connect "My Server"` | `My Server` | correct — no colon in the value |

The last row is why this has survived. A value only breaks if it contains `:` or `=`, so the
switches whose values are names work and the switches whose values are paths do not.

**The tests do not catch it.** `CommandLineParserTests` exercises the space-separated form as
`["--cons", "%TESTVAR%\\confCons.xml"]` — a path with no colon in it. It passes, and it passes for
the same reason a user's `C:\...` fails.

### Why this is worse than a rejected argument

The switch is not rejected; it is silently ignored, and what happens next depends on what else is on
the machine.

`ParseCustomConnectionPathArg` leaves `CustomConnectionFile` null, so
`ConnectionsService.GetStartupConnectionFileName` falls through to discovering candidates in the
well-known locations. If none exists the user gets *"The connection file could not be found"* about a
file sitting exactly where they said it was — confusing, but safe.

If one does exist, **the user is silently given a different connection file from the one they asked
for**, with no indication that the switch was disregarded. Anything they then edit is saved into a
store they did not intend to open. That is the case worth fixing for; the confusing dialog is only
the version that fails loudly.

The one diagnostic that exists — `"Cmdline arg: custom connection file not found: true"` — goes to
the message collector, where a user meeting a modal dialog at startup will not be looking. The word
`true` in it is the whole diagnosis and reads as nonsense.

### How it was found

Writing the manual verification runbook for `replace-default-connection-file-key` §8, which told the
reader to launch with `--cons C:\mrng-verify\confCons.xml`. The instruction was wrong and the runbook
now says so and uses the colon form. This change is what removes the need for that warning.

## What Changes

- A switch value supplied as the following argument SHALL be taken **whole**, whatever characters it
  contains. `--cons C:\path\file.xml` and `--cons:C:\path\file.xml` become equivalent.
- Splitting a switch from an inline value SHALL happen at the **first** `:` or `=` after the switch
  name only, never anywhere else in the argument.
- An argument is a switch **only** when it begins with `-`, `--` or `/`. A bare value is never
  promoted to a parameter, which is what turns a mistyped path into an invented switch today.
- A switch whose value cannot be used SHALL say so where the user is looking, and name the value it
  was given rather than reporting the placeholder it substituted.
- Existing accepted forms keep working unchanged: `/switch:value`, `--switch=value`, `-switch value`,
  bare flags with no value, and quoted values.

## Non-Goals

- Replacing the interpreter with a command-line library. It is a 2002-vintage file with a plain
  contract, and swapping it out is a larger blast radius than the defect justifies.
- Changing which switches exist, or what any of them mean.
- Making the switch names case-sensitive, or tightening `-` versus `--`.

## Capabilities

Adds `command-line-arguments`. No capability currently describes how arguments are interpreted,
which is part of why the contract drifted between `CommandLineParser` — whose `ExpandSwitchValue`
explicitly handles "the value is in the next argument" — and `CmdArgumentsInterpreter`, which cannot
parse that form when the value looks like a path. Two components disagreeing about the same input is
the kind of thing a written contract exists to prevent.

## Impact

`mRemoteNG/Tools/Cmdline/CmdArgumentsInterpreter.cs`,
`mRemoteNG/Tools/Cmdline/StartupArgumentsInterpreter.cs`,
`mRemoteNG/App/CommandLineParser.cs`,
`mRemoteNGTests/App/CommandLineParserTests.cs`.

Behavioural risk is low and worth stating precisely: this makes arguments parse that do not parse
today. The compatibility question is whether anything relies on the current behaviour, and the only
way to rely on it is to depend on a bare argument becoming a parameter — which no documented switch
does and no caller in this repository does.

The single-instance path forwards arguments between processes
(`ProgramRoot.SendArgsToRunningInstance`), so both sides must agree. They already share
`CommandLineParser.GetNormalizedArguments`, and this does not change the wire format.
