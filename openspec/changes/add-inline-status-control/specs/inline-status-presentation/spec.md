## ADDED Requirements

### Requirement: An inline outcome is shown by one call, not assembled from parts

The system SHALL present the outcome of a user-requested operation through a single control that
takes a severity and a message together, and SHALL NOT require a caller to set an icon, a colour and
a text separately.

An outcome expressed across several controls can be left half-updated. That is not hypothetical: the
SQL options page clears its result text without resetting its icon, so reopening the page after a
failed test shows a red cross beside no message at all.

#### Scenario: Reporting a failure

- **WHEN** an operation fails and the caller reports it with one call
- **THEN** the message, the icon and the colour all describe that failure

#### Scenario: Clearing an outcome

- **WHEN** the outcome is cleared
- **THEN** no icon, colour or text from the previous outcome remains visible

#### Scenario: A new outcome replaces the last

- **WHEN** an operation succeeds after a previous one failed
- **THEN** nothing from the failed outcome remains visible

### Requirement: Severity is never conveyed by colour alone

The system SHALL distinguish severities by a glyph as well as by colour.

Colour alone excludes colour-blind users — around one man in twelve — and is lost entirely when a
screenshot is printed or pasted in greyscale, which is how status lines usually reach a support
ticket.

#### Scenario: Success and failure side by side

- **WHEN** a success and a failure are rendered
- **THEN** they differ by glyph, and not only by colour

#### Scenario: Rendered without colour

- **WHEN** the strip is rendered with colour removed
- **THEN** the severity is still distinguishable

### Requirement: Status colours come from the active theme

The system SHALL resolve severity colours from the active theme's palette when one is loaded, and
SHALL NOT use hardcoded colour literals.

A hardcoded `Color.Green` is picked against one background. mRemoteNG ships dark themes, and the
Google Drive page currently paints its status with exactly such literals.

#### Scenario: An extended theme is active

- **WHEN** a status is shown while an extended theme is loaded
- **THEN** its colours come from that theme's palette

#### Scenario: No extended theme is active

- **WHEN** no extended theme is loaded
- **THEN** the status is still legible against the page background

### Requirement: The strip sizes itself to its message

The system SHALL size the status control to fit its message, including a message spanning several
lines, and SHALL NOT require the host page to reserve a fixed area for it.

The SQL options page could not display its own failure message: `BuildTestFailedMessage` returns two
lines into a label fixed at 124x13. Fixed placement also does not survive display scaling — controls
added after `InitializeComponent` keep unscaled coordinates, which at 150% put them behind a sibling.

#### Scenario: A multi-line message

- **WHEN** a message spanning two lines is shown
- **THEN** the whole message is visible

#### Scenario: A longer message replaces a shorter one

- **WHEN** a longer message replaces a shorter one
- **THEN** the strip grows and its neighbours move rather than being overlapped

#### Scenario: Displayed at a scaling factor above 100%

- **WHEN** the host page is rendered at a display scaling above 100%
- **THEN** the strip stays within its host and does not overlap a sibling control
