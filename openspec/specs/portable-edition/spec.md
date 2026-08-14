# portable-edition Specification

## Purpose

Which edition mRemoteNG is running as, and therefore where a user's settings, logs, layouts and
connection file live, and whether a connection file it protects is bound to the machine it is on.

The edition is an attribute of an *installation*, not of a build: one set of binaries is both, and
the answer is a marker file anyone can see, check and reverse without a compiler. Nothing described
this before, which is part of how a switch that decides where every user's data lives was defined in
six places in a csproj, asserted by no test, and wrong in every artefact the project shipped.

## Requirements
### Requirement: The edition is decided at runtime by a marker file

The system SHALL determine whether it is the portable edition from the presence of a `portable.flag`
file beside the executable, and SHALL NOT determine it from how the assembly was compiled.

An edition fixed at compile time is a property of the build rather than of the installation, so one
build cannot serve both and nobody can tell which they have without reading the build files. It also
means the wrong configuration shipping is invisible: every artefact of this project was built from a
configuration that defined the portable constant, so every user ran the portable edition and every
behaviour reserved for the installed edition was unreachable.

#### Scenario: The marker is present

- **WHEN** a `portable.flag` file exists beside the executable
- **THEN** the application runs as the portable edition

#### Scenario: The marker is absent

- **WHEN** no such file exists
- **THEN** the application runs as the installed edition

#### Scenario: The marker is empty

- **WHEN** the marker file has no content
- **THEN** it still selects the portable edition

Presence is the whole signal. A marker whose content had to parse would produce a file that looks
right and is ignored, with nothing to tell the person who created it which of the two it was.

#### Scenario: The location cannot be determined

- **WHEN** the directory holding the executable cannot be resolved or read
- **THEN** the application runs as the installed edition

Installed is the recoverable way to be wrong: settings land under the user's profile, which is
writable and visible. The reverse writes beside an executable that may sit in a location the user
cannot write to, and fails later and further from the cause.

### Requirement: The edition is resolved once per run

The system SHALL resolve the edition once and use the same answer for the whole run.

The answer selects where settings, logs and layouts are read from and written to. An answer that
changed mid-session would split that session's state across two locations, leaving half the user's
configuration in each with nothing to say so.

#### Scenario: The marker changes while running

- **WHEN** the marker is created or deleted while the application is running
- **THEN** the edition in force does not change until the application is started again

### Requirement: Every edition-dependent behaviour reads the same answer

The system SHALL derive all edition-dependent behaviour — settings location, log location, layout
and external-tool locations, registry cleanup on exit, and whether a connection file is given a
machine-bound protector — from that single answer.

Two sources of truth for one question is what the compile constant already was: the csproj said one
thing, and any test or reader that assumed the other was quietly wrong.

#### Scenario: Settings location follows the edition

- **WHEN** the application runs as the portable edition
- **THEN** settings are read from and written to the folder beside the executable

#### Scenario: Connection file protection follows the edition

- **WHEN** the application runs as the portable edition
- **THEN** a connection file it protects is given no machine-bound protector

This is the behaviour whose absence exposed the defect. The portable edition writes no machine-bound
protector by design, so a shipped build that was portable when it should not have been would prompt
every user for a recovery password on every open — the one property the connection-file design rests
on being acceptable to live with.

