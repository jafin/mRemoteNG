# Tasks

Ordered so the control is proven before anything depends on it, and so each page conversion is a
separate reviewable step. Sections 2 and 3 are independent of each other.

## 1. The control

- [ ] 1.1 Add `StatusSeverity` — `None`, `Working`, `Succeeded`, `Warning`, `Failed`.
- [ ] 1.2 Add `MrngStatusStrip` in `mRemoteNG/UI/Controls/`, exposing `Show(StatusSeverity, string)` and `Clear()` and **no** separate icon, colour or text setters. The single call is the design, not a convenience — see design.md.
- [ ] 1.3 Compose it from a `TableLayoutPanel` so it auto-sizes to its message; a host docks it and never positions it.
- [ ] 1.4 Resolve colours from `ThemeManager.ActiveTheme.ExtendedPalette`, with a fallback that stays legible on light and dark when no extended theme is loaded. No colour literals.
- [ ] 1.5 Give each severity a distinct glyph, reusing existing resources (`Test_16x`, `LogError_16x`, `Loading_Spinner`) so this adds no new artwork.
- [ ] 1.6 Tests: one call sets all three facets; `Clear` leaves nothing behind; a success after a failure retains nothing from the failure; severities differ by glyph and not only colour; a two-line message is fully visible; colours track a swapped theme.

## 2. SQL Server options page

- [ ] 2.1 Replace `imgConnectionStatus` + `lblTestConnectionResults` with one strip. This is the site with twenty-two paired writes and the live stale-icon defect at `SqlServerPage.cs:291`.
- [ ] 2.2 Replace `lblEncryptionStatus` with a strip, mapping legacy encryption to `Warning` and an unreadable database to `Failed`. **The upgrade button stays where it is** — it is an action, not a status, and belongs beside the strip rather than inside it.
- [ ] 2.3 Move this page's literal status strings into `Language.resx`: `"SQL connection timed out (30s)"`, `"Creating database '{0}'..."`, `"Database '{0}' created. ..."`, `"Database created but connection test failed."`, `"Failed to create database: {0}"`.
- [ ] 2.4 Confirm the existing page tests still hold — `SqlServerPageEncryptionStatusTests` asserts on `lblEncryptionStatus` by name and on the geometry of `pnlEncryptionStatus`, so both will need to follow the control rather than be deleted.

## 3. Google Drive options page

- [ ] 3.1 Replace `_lblStatus` with a strip, removing the `Color.Green` / `Color.Red` literals at `GoogleDrivePage.cs:177,182`. This page currently has no icon at all, so it also gains the non-colour signal it never had.
- [ ] 3.2 Move `"Testing..."`, `"Connection failed"`, `"Connected as {0}"` and `"Error: {0}"` into `Language.resx`.

## 4. Verification

- [ ] 4.1 Full build; zero new analyzer warnings.
- [ ] 4.2 Full test suite; zero failures.
- [ ] 4.3 `openspec validate add-inline-status-control --strict`.
- [ ] 4.4 Manual, **at a scaling factor other than 100%**: both pages, each severity, and a failure long enough to wrap. Three defects on the SQL page came from layout that was only correct at 100%, so testing at 100% alone would not have caught any of them.
- [ ] 4.5 Manual, on a dark theme: every severity legible, no colour that was chosen for a light background.
- [ ] 4.6 Re-take any `docs-website/docs/` screenshot showing a status line whose appearance changed.
