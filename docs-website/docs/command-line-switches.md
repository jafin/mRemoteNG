---
title: Command-Line Switches
---

## Syntax

A switch may be written with `/`, `-` or `--`, and its value may be given after a space, a colon or
an equals sign. These are all the same instruction:

```
mRemoteNG.exe --cons C:\stores\confCons.xml
mRemoteNG.exe --cons:C:\stores\confCons.xml
mRemoteNG.exe --cons=C:\stores\confCons.xml
mRemoteNG.exe /cons:C:\stores\confCons.xml
```

Switch names are not case-sensitive. Wrap a value containing spaces in quotes:
`--connect "My Server"`.

:::info Version

Passing an absolute path after a space — `--cons C:\stores\confCons.xml` — is new. In earlier
versions the value was split at the drive letter's colon and the switch was silently ignored, so
only the `--cons:` and `/cons:` forms worked with a full path. The same applied to any value
containing `:` or `=`, such as `--quickconnect server.example.com:2222`.

:::

A value is taken exactly as written, so anything may appear inside it. Text that is not a switch and
does not follow one is ignored.

## Connection file, settings and log

`/cons PathToConnectionsFile` `/c PathToConnectionsFile`

> Loads the connections file from the given path, for this session only. The path may be a full file
> path, or relative to the current directory, the mRemoteNG application directory, or the default
> connection file directory.
>
> If no file exists at that path, mRemoteNG says so and asks what to do — create a new connections
> file there, choose a different path, import, or exit. It does **not** quietly open your usual
> connections file instead.

`/cfg PathToSettingsFolder` `/settings` `/settingspath` `/config` `/configpath`

> Reads and writes settings in the given folder instead of the default one.

`/log PathToLogFile` `/logpath` `/logfile`

> Writes the log to the given file. A path ending in a separator is treated as a folder, and
> `mRemoteNG.log` is written inside it.

## Opening connections at startup

`/connect NameOrId`

> Opens the named connection as soon as mRemoteNG starts. Useful for desktop shortcuts. Quote names
> containing spaces: `--connect "My Server"`.

`/startup NameOrId`

> The same, but opened after the main window has finished loading.

`/quickconnect Host` `/qc Host`

> Opens an ad-hoc connection without saving it, in the same formats the Quick Connect toolbar
> accepts: `host`, `host:port` or `user@host:port`.

`/protocol Protocol` `/p Protocol`

> The protocol for `/quickconnect` — for example `RDP`, `SSH2` or `VNC`. Defaults to your configured
> Quick Connect protocol.

`/exitafter`

> Closes mRemoteNG when the last connection opened by `/connect`, `/startup` or `/quickconnect` is
> closed.

## Resetting the interface

`/reset`

> Resets window position, panels and toolbars

`/resetpos` `/rp`

> Reset the windows position

`/resetpanels` `/rpnl`

> Resets all panel positions. Use this if you have troubles with panel layouts

`/resettoolbar` `/rtbr`

> Resets the positions of all toolbars

`/noreconnect` `/norc`

> Temporary disables reconnect to previously opened sessions. Use this if you have problems opening
> mRemoteNG after you enabled the setting and restarted mRemoteNG

:::tip Already running?

With **Tools → Options → Startup/Exit → Allow only a single instance of the application** enabled, starting mRemoteNG
again passes the switches to the copy that is already open, so `--connect` and `--quickconnect`
open there rather than in a second window.

:::
