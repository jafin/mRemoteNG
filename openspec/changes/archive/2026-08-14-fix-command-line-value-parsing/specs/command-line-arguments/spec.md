## ADDED Requirements

### Requirement: A switch value is taken whole, whatever it contains

The system SHALL treat the argument following a switch as that switch's value in its entirety, and
SHALL NOT split it on any character it happens to contain.

Windows paths contain a colon after the drive letter, and `host:port` contains one by construction.
A parser that splits on a colon anywhere cannot accept either as a value, which rules out the whole
class of switch whose value is a path — the switches most likely to be given one.

#### Scenario: An absolute path as a separate argument

- **WHEN** the application is started with `--cons` followed by `C:\stores\confCons.xml`
- **THEN** the connection file switch has the value `C:\stores\confCons.xml`

#### Scenario: A value containing a port

- **WHEN** the application is started with `--quickconnect` followed by `server.example.com:2222`
- **THEN** the quick connect switch has the value `server.example.com:2222`

#### Scenario: A value containing an equals sign

- **WHEN** a switch value supplied as a separate argument contains `=`
- **THEN** the value is taken whole, including the `=`

### Requirement: An inline value is separated at the first separator only

The system SHALL split a switch from an inline value at the first `:` or `=` following the switch
name, and SHALL leave every later occurrence in the value.

#### Scenario: An inline path

- **WHEN** the application is started with `--cons:C:\stores\confCons.xml`
- **THEN** the switch is `cons` and the value is `C:\stores\confCons.xml`

#### Scenario: The other accepted prefixes and separators

- **WHEN** a switch is written as `/cons:<value>`, `--cons=<value>` or `-cons <value>`
- **THEN** it is interpreted identically to `--cons:<value>`

#### Scenario: A quoted value

- **WHEN** a value is enclosed in single or double quotes
- **THEN** the enclosing quotes are removed and the rest of the value is unchanged

### Requirement: Only an argument with a switch prefix is a switch

The system SHALL treat an argument as a switch only when it begins with `-`, `--` or `/`, and SHALL
NOT create a parameter from an argument that does not.

An argument that is not a switch and has no switch waiting for it is discarded. Promoting it to a
parameter invents a switch nobody defined and cannot be distinguished from a typo, so a mistake
produces no error and no effect.

#### Scenario: A value with no switch waiting for it

- **WHEN** an argument that does not begin with a switch prefix appears where no switch is awaiting a value
- **THEN** no parameter is created from it

#### Scenario: A switch given no value

- **WHEN** a switch is followed by another switch rather than by a value
- **THEN** the first switch is recorded as present with no value
- **AND** the second switch is recorded as a switch rather than consumed as the first one's value

### Requirement: A switch value that cannot be used is reported in the user's terms

The system SHALL report an unusable switch value by naming the value that was supplied, and SHALL
NOT report an internal placeholder in its place.

The failure is met at application startup, where the only thing the user has to work with is what
they typed. A message naming something they did not type is worse than no message, because it sends
them looking for a fault that is not there.

#### Scenario: A connection file switch naming a file that does not exist

- **WHEN** `--cons` names a path that does not exist
- **THEN** the reported message contains that path

#### Scenario: The store that opens instead

- **WHEN** a connection file switch cannot be honoured
- **THEN** a different connection file is not opened silently in its place

This is the consequence that makes the defect worth fixing rather than documenting. A user who asks
for one store and is given another without being told will edit and save into a file they did not
intend to open.
