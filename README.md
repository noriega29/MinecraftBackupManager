# Minecraft Backup Manager

A backup and restoration management tool for Minecraft Java Edition servers on Windows.

Minecraft Backup Manager is a Windows console application designed to simplify and automate backup and restoration workflows for Minecraft Java Edition servers.

The application communicates with Minecraft servers through **RCON** to coordinate safe server operations before backups, and uses **Restic** as its backup engine.

## Table of Contents

- [Features](#features)
- [How Backups Work](#how-backups-work)
- [Backup Types and Retention](#backup-types-and-retention)
- [Project Status](#project-status)
- [Architecture](#architecture)
- [Technology Stack](#technology-stack)
- [Requirements](#requirements)
- [License](#license)

## Features

Features are grouped by their current development status.

### Implemented

The backup engine is implemented and covered by automated tests. It is not yet reachable from the console menus (see [In Development](#in-development)).

**Core Infrastructure**
- Persistent application configuration using JSON, with validation and schema versioning.
- Centralized management of application paths.
- Categorized logging with multiple severity levels.
- Secure credential storage using Windows Data Protection API (DPAPI).

**Minecraft Server Integration**
- RCON client implemented from scratch: connection, authentication, asynchronous command execution, timeouts, and packet serialization and deserialization.
- Server preparation for backups: temporarily disabling automatic saves and flushing pending world data. Automatic saves are always re-enabled afterwards, even if the backup fails or is canceled.
- Stopped-server detection: if the RCON connection is refused, the application checks the world's `session.lock` file. If the world is free, the backup runs without RCON; if it is locked, a clear error is reported.

**Backup Engine (Restic)**
- Restic integration: repository initialization and state checks, backup, snapshot listing, stale lock cleanup, retention (`forget`) and compaction (`prune`).
- Encrypted, deduplicated and versioned snapshots, tagged by backup type.
- Bundled or custom Restic executable.
- Locked files do not abort a backup: the snapshot is created and the files that could not be read are reported as warnings.

**Backup Coordination**
- One backup at a time, with structured reports for every result (completed, completed with warnings, failed, already running, skipped).
- Retention policy by backup type (see [Backup Types and Retention](#backup-types-and-retention)).
- Optional setting to skip automatic backups while the server is off.

**Automatic Scheduling**
- Built-in hourly scheduler, at minute :00 by default.
- The type of each automatic backup (weekly, daily or hourly) is chosen from the existing snapshots.
- Automatic backups can be paused and resumed. The pause is kept in memory only, so restarting the application re-enables them.
- Cycles missed while the computer was off or suspended are skipped, not recovered.

### In Development

**Console Interface**
- Configuration menu (paths, passwords and options) and first-run setup flow.
- Backup menu: initial backup, manual backup, and pause or resume of automatic backups.
- Connecting the scheduler to the application.

Currently only the information menu is functional; the configuration, backup and restoration menus are placeholders.

### Planned

**Restoration Management**
- Restore complete server backups.
- Select specific snapshots for restoration.
- Create pre-restoration backups.
- Validate restoration operations and check repository integrity to help reduce the risk of data loss.

**Background Operation**
- Keep running in the notification area so automatic backups continue after the window is closed.
- Start automatically with Windows.

**Maintenance**
- Log size management.

## How Backups Work

1. The Restic repository is checked before anything touches the server.
2. The application connects to the server through RCON.
3. Automatic saves are disabled (`save-off`) and pending world data is written to disk (`save-all flush`).
4. Restic creates a snapshot of the server folder.
5. Automatic saves are re-enabled (`save-on`). This always happens, even if an error occurs or the backup is canceled.
6. The retention policy is applied to automatic backups.
7. The result is reported and written to the backup log.

If the server is off, steps 2, 3 and 5 are skipped and the backup is taken directly, after confirming that the world is not in use.

## Backup Types and Retention

Every snapshot is tagged with its type:

| Type | Created | Retention |
| --- | --- | --- |
| `initial` | First backup after setup | Never deleted automatically |
| `manual` | On demand | Never deleted automatically |
| `hourly` | Automatically, when no weekly or daily backup is due | Last 6 by default (1 to 24) |
| `daily` | First automatic backup of each day | Last 7 by default (up to 7) |
| `weekly` | First automatic backup of each week (Monday to Sunday) | Last 4 by default (up to 4) |

Old snapshots are removed after each automatic backup, and the repository is compacted at most once a day. Retention counts are stored in the configuration file.

## Project Status

🚧 **In Development**

The core infrastructure, Minecraft RCON integration, Restic integration, and the backup and scheduling engine are implemented and tested. The console menus that expose them and the restoration workflow are still under development. Features and documentation may change as the project progresses.

## Architecture

The application is organized into components with distinct responsibilities:

| Component | Responsibility |
| --- | --- |
| `Core` | Application startup and coordination. |
| `UI` | Console-based application menus. |
| `Configuration` | Loading, saving, and validating application settings. |
| `Infrastructure` | Centralized application paths and infrastructure services. |
| `Logging` | Categorized application logging. |
| `Security` | Secure storage of sensitive credentials. |
| `Minecraft` | RCON communication, server preparation for backups, and stopped-server detection. |
| `Restic` | Execution and interpretation of Restic operations. |
| `Backup` | Backup coordination, retention, backup type selection, and scheduling. |

Automated tests are maintained in the `MinecraftBackupManager.Tests` project.

## Technology Stack

- **Language:** C#
- **Framework:** .NET 10
- **IDE:** Visual Studio 2022
- **Server communication:** Minecraft RCON
- **Credential protection:** Windows Data Protection API (DPAPI)
- **Backup engine:** Restic
- **Testing:** MSTest

## Requirements

- Windows operating system.
- .NET 10 runtime.
- A Minecraft Java Edition server with RCON enabled and configured.
- The Restic executable, developed and tested with version 0.19.1. The application looks for `Tools\restic.exe` next to the executable, or uses a custom path set in the configuration. The executable is not included in this repository.
- A backup repository location outside the server folder, preferably on a different drive.

> **Important:** the Restic repository password is stored only in the application's encrypted credential file. Without it the backups cannot be opened, so keep a copy in a safe place.

## License

This project is licensed under the [MIT License](LICENSE).

Copyright © 2026 Mateo Noriega.