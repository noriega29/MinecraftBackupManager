# Minecraft Backup Manager

A backup and restoration management tool for Minecraft Java Edition servers on Windows.

Minecraft Backup Manager is a Windows console application designed to simplify and automate backup and restoration workflows for Minecraft Java Edition servers.

The application communicates with Minecraft servers through **RCON** to coordinate safe server operations before backups. **Restic** is the planned backup engine; its integration is currently under development.

## Table of Contents

- [Features](#features)
- [Project Status](#project-status)
- [Architecture](#architecture)
- [Technology Stack](#technology-stack)
- [Requirements](#requirements)
- [License](#license)

## Features

Features are grouped by their current development status.

### Implemented

**Core Infrastructure**
- Persistent application configuration using JSON.
- Configuration validation and schema versioning.
- Centralized management of application paths.
- Categorized logging with multiple severity levels.
- Secure credential storage using Windows Data Protection API (DPAPI).

**Minecraft Server Integration**
- RCON connection and authentication.
- Asynchronous RCON command execution.
- RCON packet serialization and deserialization.
- Server preparation for backup operations, including temporarily disabling automatic saves, flushing pending world data, and restoring automatic saves.

### In Development

**Backup Engine Integration**
- Integration with Restic.
- Backup creation and snapshot management.
- Deduplicated and versioned backups.
- Backup categories and retention policies.

### Planned

**Restoration Management**
- Restore complete server backups.
- Select specific snapshots for restoration.
- Create pre-restoration backups.
- Validate restoration operations to help reduce the risk of data loss.

**Automation**
- Schedule automatic backup operations through Windows Task Scheduler.
- Run scheduled operations without requiring manual interaction.

## Project Status

🚧 **In Development**

The core infrastructure and Minecraft RCON integration have been implemented. Restic integration and the backup and restoration workflows are still under development. Features and documentation may change as the project progresses.

## Architecture

The application is organized into components with distinct responsibilities:

| Component | Responsibility |
| --- | --- |
| `Core` | Application startup and coordination. |
| `Configuration` | Loading, saving, and validating application settings. |
| `Infrastructure` | Centralized application paths and infrastructure services. |
| `Logging` | Categorized application logging. |
| `Minecraft` | Minecraft server coordination and RCON communication. |
| `Security` | Secure storage of sensitive credentials. |
| `Restic` | Planned backup-engine integration; currently under development. |
| `UI` | Console-based application menus. |

Automated tests are maintained in the `MinecraftBackupManager.Tests` project.

## Technology Stack

- **Language:** C#
- **Framework:** .NET 10
- **IDE:** Visual Studio 2022
- **Server communication:** Minecraft RCON
- **Credential protection:** Windows Data Protection API (DPAPI)
- **Testing:** MSTest
- **Backup engine:** Restic (integration in development)
- **Automation:** Windows Task Scheduler (planned)

## Requirements

### Current Requirements

- Windows operating system.
- .NET 10 runtime.
- A Minecraft Java Edition server.
- RCON enabled and configured on the Minecraft server.

### Additional Requirement for Backup Integration

- Restic executable (required when Restic integration becomes available).

## License

This project is licensed under the [MIT License](LICENSE).

Copyright © 2026 Mateo Noriega.
