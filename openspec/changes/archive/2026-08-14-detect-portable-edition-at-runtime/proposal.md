## Why

Whether mRemoteNG runs as the portable edition was a **compile-time constant**, and it was defined by
almost every configuration:

| Configuration | `DefineConstants` | Built by |
|---|---|---|
| `Debug`, `Release` | **PORTABLE** | `build.ps1`, every CI workflow, the MSI harvest |
| `Release Portable`, `Release Self-Contained` | **PORTABLE** | `build.ps1 -Portable` |
| `Release Installer` | *(no PORTABLE)* | **nothing** |
| `Deploy to github` | *(no PORTABLE)* | **nothing** |

Every workflow builds `-p:Configuration=Release`, and `Build_mR-NB.yml` step 09a harvests
`mRemoteNG\bin\x64\Release` into the MSI. So **every artefact this project ships — nightly zip,
stable zip, and the installer — runs as the portable edition**, and the only configuration that does
not is built by nothing.

The consequences are not cosmetic:

- `Runtime.IsPortableEdition` is true for every user, so settings, logs and layouts live beside the
  executable — including for people who installed via MSI into Program Files.
- Anything gated on the edition is **unreachable**. `replace-default-connection-file-key` writes no
  machine-bound protector for the portable edition by design, so under that change every user of
  every shipped build would be asked for a recovery password on every open. "Daily use asks for
  nothing" is the property that whole design rests on being acceptable to live with, and it would
  reach nobody.
- Telling the two editions apart meant reading the csproj. This cost a full manual-verification
  session to diagnose: a store kept coming back with no machine protector, the policy was correct,
  the path was correct, and the answer was in a build file.

`build.ps1 -Portable` did not help. It passes `-p:DefineConstants=PORTABLE` on top of
`-p:Configuration=Release` — a constant `Release` already had — so its real effects are
`SelfContained=true` and a different publish folder. It changes how the application is *packaged*,
never what edition it *is*. Both builds were portable; one merely travelled better.

## What Changes

- The edition is decided at runtime by a **`portable.flag` marker file beside the executable**.
  Present means portable; absent means installed.
- The answer SHALL be resolved once per run. It selects where settings, logs and layouts are read
  and written, so a value that changed mid-session would split that session's state across two
  locations.
- Every `#if PORTABLE` site becomes a runtime branch. The settings provider — the one decision a
  constant genuinely was needed for, because `ChooseProvider` selected a **base class** — moves to
  `PortableSettingsInitializer`, which already constructed and wired the provider at runtime before
  anything read a setting.
- `PORTABLE` is removed from the plain `Debug|*` and `Release|*` configurations, which is what makes
  shipped artefacts installed-edition. It is **kept** on `Release Portable` and
  `Release Self-Contained` as a transition, so `build.ps1 -Portable` keeps producing a portable build
  before packaging learns to write the marker.
- One build can serve both editions, which is what packaging wants: the zip carries the marker, the
  installer does not.

## Non-Goals

- Changing what the portable edition *does*. Every behaviour it selects is unchanged; only how the
  selection is made.
- Deciding where the MSI installs, or any other packaging layout question.
- Making the marker a security control. It selects file locations and whether a connection file is
  given a machine-bound protector; it decrypts nothing. Anyone who can write it beside the
  executable can replace the executable.

## Capabilities

Adds `portable-edition`. Nothing described this before, which is part of how a switch that changed
where every user's data lives ended up defined in six places in a csproj and asserted by no test.

## Impact

`mRemoteNG/App/Info/PortableEdition.cs` (new), `mRemoteNG/App/Runtime.cs`,
`mRemoteNG/Config/Settings/Providers/ChooseProvider.cs`,
`mRemoteNG/Config/Settings/Providers/PortableSettingsInitializer.cs`,
`mRemoteNG/Config/Settings/DockPanelLayoutLoader.cs`,
`mRemoteNG/Config/Settings/ExternalAppsLoader.cs`,
`mRemoteNG/Tools/IeBrowserEmulation.cs`, `mRemoteNG/UI/Forms/FrmAbout.cs`,
`mRemoteNG/mRemoteNG.csproj`.

**Packaging is the part this change cannot finish**, and it must land before the transitional
constant comes out:

- `build.ps1` must write `portable.flag` into the portable output. It is off-limits to agents under
  the repository's own rules, so it is handed over rather than edited here.
- The release workflow must include the marker in the portable zip and keep it out of the MSI.

Until then the transitional constant covers `build.ps1 -Portable`, so nothing regresses in the
meantime — but a portable zip built from plain `Release` would silently be an installed build, which
is precisely the failure in reverse.

**Upgrade note.** Existing users of nightly and stable are running a portable build with settings
beside the executable. After this, a build without a marker looks in `%APPDATA%` instead and their
settings appear to have vanished. Shipping `portable.flag` inside the portable zip is what prevents
that, since upgrading means extracting over the old copy. There is deliberately no heuristic that
infers portability from an existing settings folder: implicit detection is exactly what made the
original defect so hard to see, and a wrong guess would move a user's data without being asked.
