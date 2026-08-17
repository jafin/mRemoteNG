## Why

Every page that performs an action and reports the result in place does it by hand, and each one
does it differently.

**`SqlServerPage` sets an icon and a label as two separate statements, twenty-two times.**

```csharp
UpdateConnectionImage(false);
lblTestConnectionResults.Text = BuildTestFailedMessage(...);
```

Two facts about one outcome, kept in two controls, updated by convention. The convention is already
broken in the live code: `LoadSettings` clears the text —

```csharp
lblTestConnectionResults.Text = "";        // SqlServerPage.cs:291
```

— and never touches the icon. Reopening the options window after a failed test therefore shows a red
cross with no message beside it, which reads as "something is wrong" and says nothing about what.

**`GoogleDrivePage` has no icon at all and paints the label instead:**

```csharp
_lblStatus.ForeColor = Color.Green;        // GoogleDrivePage.cs:177
_lblStatus.ForeColor = Color.Red;          // GoogleDrivePage.cs:182
```

Hardcoded, so they ignore the theme entirely — on a dark theme these are the two colours nobody
picked — and colour is the *only* signal, so the difference between success and failure is invisible
to a colour-blind user and to anyone reading a screenshot in greyscale.

**The layout carries the same duplication.** Both pages positioned status text at fixed coordinates
with a fixed width, which is how `SqlServerPage` ended up unable to display its own failure message:
`BuildTestFailedMessage` returns two lines into a label sized `124x13`. That was fixed on the SQL
page by moving it into a layout panel; the next page to grow a status line starts the same cycle.

None of this is dangerous. It is the kind of thing that stays wrong for years because each instance
is individually too small to justify a change.

## What Changes

- A single `MrngStatusStrip` control taking **one call** — a severity and a message — and deriving
  the icon, the colour and the layout from the severity.
- Severity colours come from the active theme's palette, with a legible fallback when no extended
  theme is loaded. No control hardcodes a colour.
- Icon *and* text always change together, because there is one call and one control. The
  stale-icon defect stops being possible rather than being fixed.
- The control auto-sizes, so a two-line failure message is a two-line control rather than clipped
  text.
- `SqlServerPage` (test connection, encryption status) and `GoogleDrivePage` (Drive connection test)
  adopt it. Both keep their current wording.
- Status strings that are currently C# literals move to `Language.resx` as they are touched.

## Impact

- New: `mRemoteNG/UI/Controls/MrngStatusStrip.cs`, plus tests.
- Changed: `SqlServerPage`, `GoogleDrivePage`. `FrmFind`, `FrmPassword`, `FrmGroupThumbnail` and
  `UpdateWindow` also hold status labels but are **out of scope** — see design.md, "What is not a
  status strip".
- No behaviour change a user can act on: the same operations report the same outcomes, more
  consistently and more legibly.
- Screenshots in `docs-website/docs/` that show a status line will need re-taking if the appearance
  changes materially.
