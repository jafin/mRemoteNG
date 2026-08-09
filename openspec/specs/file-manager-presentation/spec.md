# file-manager-presentation Specification

## Purpose
TBD - created by archiving change sftp-panel-usability. Update Purpose after archive.
## Requirements
### Requirement: Each entry's kind is shown as text and as an icon

The listing SHALL show a column giving each entry's kind as `Folder`, `File` or `Link`, and SHALL show
an icon beside the name distinguishing a directory from a file. An entry that is a symbolic link or a
reparse point SHALL be reported as `Link` in the column.

Name alone does not say what an entry is — a directory called `backup.tar` and a file called `backup`
look identical in a list of names. The icon answers it at a glance for the common case and the column
answers it exactly, including the case an icon cannot show.

#### Scenario: A directory

- **WHEN** a directory is listed
- **THEN** its kind reads `Folder`
- **AND** a folder icon is shown beside its name

#### Scenario: A file

- **WHEN** a file is listed
- **THEN** its kind reads `File`
- **AND** a file icon is shown beside its name

#### Scenario: A link

- **WHEN** an entry is a symbolic link or a reparse point
- **THEN** its kind reads `Link`
- **AND** it still carries an icon

#### Scenario: Both panes

- **WHEN** the local and the remote pane both list entries
- **THEN** both show the kind column and the icons

### Requirement: A parent row navigates up from the listing

The listing SHALL show a `..` row above the entries, which navigates to the parent directory when
activated. It SHALL NOT be shown when the listing is at the filesystem root.

The parent row SHALL NOT be treated as a file: it SHALL be excluded from the selection that transfer,
rename and delete act on, SHALL NOT be counted in the item count or the size total, and SHALL show
nothing in the kind, size and modified columns — it describes a destination, not an entry with
attributes of its own.

Every file manager has had this row for decades, and reaching for it is reflex. Its danger is that
anything treating it as an ordinary entry would offer to transfer or delete it, so it is excluded from
those commands by construction rather than by the user noticing.

#### Scenario: Navigating up

- **WHEN** the user activates the `..` row
- **THEN** the pane lists the parent directory

#### Scenario: At the root

- **WHEN** the listing is at the filesystem root
- **THEN** no `..` row is shown

#### Scenario: The parent row is not transferable

- **WHEN** the `..` row is selected and the user transfers, renames or deletes the selection
- **THEN** the parent row is not included in the operation

#### Scenario: The parent row is not counted

- **WHEN** a directory containing three entries is listed
- **THEN** the summary reports three items
- **AND** the size total ignores the parent row

#### Scenario: The parent row stays at the top

- **WHEN** the listing is sorted by any column, in either direction
- **THEN** the `..` row remains above the entries

#### Scenario: The parent row carries no attributes

- **WHEN** the `..` row is shown
- **THEN** its kind, size and modified cells are all empty

### Requirement: Modified dates are aligned

The Modified column SHALL zero-pad the day, month and hour of each date, and SHALL keep the field
order and separators of the user's locale.

An unpadded column is ragged — `1/2/2026` and `11/12/2026` start their year in different places — which
makes a column of dates hard to scan. Padding fixes the alignment; reordering the fields would fix it
too but would show a British user an American date, which is worse than ragged.

#### Scenario: A single-digit day and month

- **WHEN** an entry was modified on the second of January
- **THEN** the day and month are shown with a leading zero

#### Scenario: Locale order is preserved

- **WHEN** the user's locale puts the day before the month
- **THEN** the date is shown day before month

### Requirement: Directories show no size

The Size column SHALL be blank for a directory.

A directory has no size in any sense the column means, and SFTP reports zero for one. Showing `0 B`
states something false: that the directory was measured and found empty.

#### Scenario: A directory

- **WHEN** a directory is listed
- **THEN** its size cell is empty

#### Scenario: An empty file

- **WHEN** a file of zero bytes is listed
- **THEN** its size reads `0 B`

#### Scenario: The parent row

- **WHEN** the `..` row is shown
- **THEN** its size cell is empty

