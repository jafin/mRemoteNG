# Design

## Context

One class parses arguments for the whole application:

```csharp
Regex spliter = new("^-{1,2}|^/|=|:", RegexOptions.IgnoreCase | RegexOptions.Compiled);
...
string[] parts = spliter.Split(txt, 3);
```

`CmdArgumentsInterpreter.cs:32,45`. It then switches on `parts.Length`:

| Parts | Meaning assigned | Reached by |
|---|---|---|
| 1 | a value for the parameter that is waiting | `MyServer` |
| 2 | a new parameter with no value yet | `--cons`, and **`C:\path`** |
| 3 | a parameter with an inline value | `--cons:C:\path` |

The whole defect is that `C:\path` and `--cons` produce the same shape. `Split` is given the raw
argument with no knowledge of whether it started with a switch character, so `^-{1,2}|^/` and `=|:`
are alternatives in one pattern when they answer two different questions: *is this a switch?* and
*where does its inline value begin?*

Two callers depend on the result, and they parse the same `args` array independently:

| Caller | Uses |
|---|---|
| `CommandLineParser` | `--cons`/`--cfg`/`--log` overrides, and `GetNormalizedArguments` for the single-instance forward |
| `StartupArgumentsInterpreter` | `CustomConnectionFile`, `--connect`, `--quickconnect`, the reset flags |

`CustomConnectionFile` takes priority over everything in
`ConnectionsService.GetStartupConnectionFileName`, so the interpreter's answer is the one that
decides which file opens.

## Goals

- `--switch value` works for every value, including paths, `host:port`, and anything with `=`.
- No accepted form stops working.
- A value that cannot be used is reported in terms of what the user typed.

## Non-Goals

- A different argument library. See the proposal.
- Any change to the set of switches or their meanings.

## Decisions

### Ask "is this a switch?" before "where is the value?"

The two questions are currently one regex. Separating them is the whole fix:

```
if the argument starts with "--", "-" or "/":
    name = up to the first ':' or '=' after that prefix
    value = the remainder if that separator was present, otherwise pending
else:
    it is a value, and belongs to the pending switch (or is ignored)
```

A bare argument is then never a switch, whatever it contains, and the drive-letter colon is just a
character inside a value.

### Only the first separator splits

`--cons:C:\path` must yield `cons` and `C:\path`, not `cons` and `C`. `Split(txt, 3)` gets this right
today by accident — the count stops it after two splits — and the accident is load-bearing. Making it
explicit ("the first `:` or `=` after the name") means the behaviour no longer depends on a limit
argument that reads like an optimisation.

### A bare argument with no switch waiting is ignored, not invented

Today it becomes a parameter set to `"true"`, so a typo produces a switch nobody defined and no
error. Ignoring it is what makes the failure visible: `--cons` with a missing value stays valueless
instead of silently absorbing the next switch's name.

There is a real trade here. Ignoring is silent too, and something a user typed did nothing. The
answer is the reporting decision below rather than promoting stray text to a parameter.

### Report the value, not the substitution

The existing message is:

```
Cmdline arg: custom connection file not found: true
```

`true` is the placeholder the parser invented; the user typed a path. After this change the same
message names the path, which makes it a diagnosis rather than a puzzle. It should also reach the
user rather than only the message collector, since the failure it explains is met at startup behind
a modal dialog.

**Not silently substituted.** A switch pointing at a file that does not exist must not fall back to
the default connection file as though nothing was asked for — that is exactly how "the wrong store
opened and I did not notice" happens. What the fallback should be instead is deliberately left to
implementation: refusing to start is defensible, and so is starting with an explicit message. Both
are better than today's silence.

## Risks

| Risk | Mitigation |
|---|---|
| A caller depends on a bare argument becoming a parameter | Nothing in this repository does; the only way to depend on it is to rely on the bug |
| The two parsers drift again | The spec is written against the interpreter's contract, so both callers are held to one description |
| Single-instance forwarding disagrees between versions | The forwarded form is already normalised by `CommandLineParser`; unchanged here |
| A value legitimately starting with `-` | Already ambiguous today and no worse after; out of scope, and `--switch=-value` remains available |

## Open Questions

- Should an unusable `--cons` refuse to start, or start and say so loudly? Refusing is the safer
  default for a switch whose whole purpose is "not the usual file", and it is a behaviour change
  beyond the parsing fix, so it wants deciding rather than assuming.
- Should `GetNormalizedArguments` rewrite space-separated pairs into the inline form before
  forwarding to a running instance? It would make the two processes agree by construction rather
  than by both parsing correctly. Probably yes, and it is cheap.
