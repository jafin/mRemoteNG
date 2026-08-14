# Design

## Context

`Runtime.IsPortableEdition` was `#if PORTABLE return true; #else return false;`, and eight other
sites branched on the same constant:

| Site | What it selected |
|---|---|
| `Runtime.IsPortableEdition` | the answer everything else asks for |
| `ChooseProvider` | **the base class of the settings provider** |
| `DockPanelLayoutLoader`, `ExternalAppsLoader` | whether to look in the pre-`Settings`-folder location |
| `IeBrowserEmulation` | whether to remove its registry values on exit |
| `FrmAbout` | whether the title says "Portable Edition" |

Only one of those needed a compiler. `ChooseProvider` inherited `PortableSettingsProvider` or
`LocalFileSettingsProvider` depending on the constant, and a base class cannot be chosen at runtime —
so that one decision held the whole edition hostage to build configuration.

## Goals

- One build can be either edition, decided where a user can see and change it.
- No behaviour of either edition changes.
- The failure mode is visible: someone asking "which edition am I running?" can answer it by looking
  in a folder.

## Decisions

### A marker file, not a setting

A setting would live in the settings file, whose location is one of the things the edition decides.
Reading it would mean knowing the answer before asking the question.

Beside the executable, for the same reason: it is the one location that is known before any of this
is resolved.

### Presence is the whole signal

The file's contents are not read. A marker with content that had to parse would produce a file that
looks right and is ignored, and the person who created it would have no way to tell which.

### Resolved once per run

Cached in a `Lazy<bool>`. The answer decides where settings, logs and layouts are read from and
written to; a value that changed mid-session would split that session's state across two locations
and leave the user with half their configuration in each. Creating or deleting the marker takes
effect at the next start — which is also the only moment a user could sensibly mean it.

### The provider is chosen where it can be

`PortableSettingsInitializer.EnsureInitialized()` already existed, already ran before any settings
class was read, and already constructed one provider instance and wired it to every settings class.
It was constructing `ChooseProvider` — whatever that had compiled to. Constructing
`PortableSettingsProvider` or `ChooseProvider` from `Runtime.IsPortableEdition` instead is a
two-line change, and it is the whole reason this was ever a compile-time decision.

`ChooseProvider` stays, deriving unconditionally from `LocalFileSettingsProvider`, because
`Settings.cs` names it in a `SettingsProviderAttribute`. If the initializer is ever not reached
first, the attribute decides — and installed is the safer of the two defaults, writing under the
user's profile rather than beside an executable that may sit in Program Files.

### Unknown resolves to installed

An unreadable or unresolvable directory answers "installed". A portable user who lands there finds
their settings under `%APPDATA%`, which is writable, visible, and something they can report. The
opposite has an installed edition writing beside an executable in Program Files and failing later,
further from the cause.

### The constant is kept for two configurations, on purpose

`Release Portable` and `Release Self-Contained` still define `PORTABLE`, and `Detect()` still honours
it. This is transitional and stated as such in code.

`build.ps1` is off-limits to agents under this repository's rules, so it cannot be taught to write
the marker here. Removing the constant outright would therefore turn `build.ps1 -Portable` into an
installed build the moment this landed — the same class of defect this change exists to fix, in the
opposite direction and with a worse blast radius, because it moves an existing user's settings.

The constant comes out when packaging writes the marker. That ordering is the point.

## Risks

| Risk | Mitigation |
|---|---|
| Existing portable users' settings appear to vanish | Ship `portable.flag` in the portable zip; upgrading extracts over the old copy |
| A portable zip built from plain `Release` is silently installed | The transitional constant covers `-Portable` until packaging changes; recorded as blocking |
| The MSI accidentally ships the marker | It harvests the plain `Release` output, which has no marker to harvest |
| `SettingsProviderAttribute` wins over the initializer | Unchanged from before, and now falls to the safer default rather than to whatever the build compiled |
| Someone drops a marker to weaken protection | Not a boundary: it stops a *future* save writing a machine protector and takes nothing off a store that already has one. Write access beside the executable is already write access to the executable |

## Open Questions

- Should the marker's presence be logged at startup alongside the edition? The startup line already
  says "Portable Edition"; saying *why* would have shortened the session that found this from an
  hour to a minute. Leaning yes.
- Should `Release Self-Contained` be portable at all? It defines `PORTABLE` today and `build.ps1
  -SelfContained` does not use that configuration, so the two disagree about what "self-contained"
  means. Out of scope, worth someone's attention.
