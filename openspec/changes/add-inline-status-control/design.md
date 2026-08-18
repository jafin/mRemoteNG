# Design

## Inline, not the notification panel

The obvious alternative is to route these messages to `Runtime.MessageCollector` and let the
notification panel show them. It is wrong for this class of message, and worth saying why before the
next person proposes it.

A test-connection result answers a question the user asked half a second ago, by clicking a button
six inches away. Moving the answer to a docked panel elsewhere in the window separates cause from
effect, and on a page where the panel is not visible it separates the answer from the user entirely.
The notification panel is for things that happen *without* being asked for — a background save
warning about weak encryption belongs there, and already goes there.

So: inline for a result the user requested, the panel for everything else. Some messages legitimately
go to both, and the encryption status is one of them.

## One call, because two calls is the bug

The API is deliberately not `Icon`, `Text` and `Colour` properties. It is:

```csharp
status.Show(StatusSeverity.Failed, message);
status.Clear();
```

The whole defect class this replaces comes from an outcome being expressed in more than one place:
an icon set here, a message set there, a colour set in a third statement, and any of them able to
survive a change to the others. `SqlServerPage.cs:291` is that bug sitting in the tree today — the
message is cleared and the red cross is left behind.

A single call makes the inconsistent state unrepresentable. That is the entire point of the control;
if it grows separate icon and text setters it has failed and the old bug comes back.

## Severity, not colour

`StatusSeverity` is `None`, `Working`, `Succeeded`, `Warning`, `Failed`.

Callers state what happened and never what it should look like. That is what lets the palette,
the icon set and the accessibility behaviour change in one place, and it is what stops the next
`Color.Green`.

**Colour is never the only signal.** Each severity carries a distinct glyph as well, because
red-on-green is invisible to roughly one man in twelve, and because a screenshot pasted into a
ticket is often greyscale. `Working` reuses the existing spinner.

Colours resolve from `ThemeManager.ActiveTheme.ExtendedPalette` when an extended theme is loaded, and
otherwise fall back to `SystemColors`-derived values that stay legible on both light and dark. No
literal `Color.Red` anywhere in the control or its callers.

## Layout is the control's problem, not the page's

The control is a `TableLayoutPanel`-based composite that auto-sizes to its content, so a two-line
failure message makes a two-line strip. Pages dock it; they never position it.

This is not a preference. Three separate bugs on the SQL options page this month came from
hand-placed coordinates: controls added after `InitializeComponent` do not get the designer's DPI
scaling, so at 150% they drifted behind the tab control and the upgrade button disappeared in
Advanced view. A control that owns its own layout cannot be placed wrongly by the next page that
uses it.

## What is not a status strip

Scope discipline, because "we have a status control now" invites converting every label that
happens to be called `lblStatus`:

| Site | Verdict |
|---|---|
| `SqlServerPage` test connection | **Yes** — result of a requested action |
| `SqlServerPage` encryption status | **Yes** — a standing statement about the database, with severity |
| `GoogleDrivePage` connection test | **Yes** — same shape, currently colour-only |
| `FrmGroupThumbnail` "3 connections — 1 connected" | No — a running count, no severity |
| `FrmFind` "Not found" | No — a search result, not an operation outcome |
| `FrmPassword` `lblStatus` | No — prompt text, not an outcome |
| `UpdateWindow` | Undecided — revisit once the three above have settled |

The test is whether the message has a *severity*. A count is not a severity, and forcing one on it
means picking a colour for "3 connections", which is how status controls turn into decoration.

## Localization

Several current status strings are C# literals — `"Testing..."`, `"Connection failed"`,
`"SQL connection timed out (30s)"`, `"Database created but connection test failed."` The control
cannot fix that and should not try; it takes a string. But every literal on a line being converted
moves to `Language.resx` as part of converting it, because that line is already being edited and the
alternative is that it is never done.
