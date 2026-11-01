# Minecraft Backup Manager

**A backup and restoration management tool for Minecraft Java Edition servers on Windows.**

Minecraft Backup Manager is a Windows console application designed to simplify and automate the backup and restoration of Minecraft Java Edition servers.

The application uses [Restic](https://restic.net/) as its backup engine and communicates with Minecraft servers through RCON to coordinate safe backup operations.

## Features

### Backup Management
- Create and manage Minecraft server backups.
- Coordinate server saves through RCON before backing up.
- Use Restic for deduplicated and versioned backups.
- Support different backup categories and retention policies.

### Restoration Management
- Restore complete server backups.
- Select specific snapshots for restoration.
- Support safe restoration with pre-restoration backups.
- Validate restoration operations to reduce the risk of data loss.

### Security
- Protect sensitive credentials using Windows DPAPI.
- Store application settings in a persistent configuration file.

### Automation
- Schedule automatic backups through Windows Task Scheduler.
- Execute scheduled operations without requiring manual interaction.

## Technology Stack

- **Language:** C#
- **IDE:** Visual Studio 2022
- **Framework:** .NET
- **Backup Engine:** Restic
- **Server Communication:** RCON
- **Credential Protection:** Windows DPAPI
- **Automation:** Windows Task Scheduler

## Project Status

<escape>🚧</escape> **In Development**

Minecraft Backup Manager is currently under development. Features and documentation will evolve as the project progresses.

## Requirements

The application is being developed for Windows environments and requires:

- Windows operating system.
- .NET runtime compatible with the application.
- Restic executable.
- A Minecraft Java Edition server with RCON enabled.

## License

This project is licensed under the [MIT License](LICENSE).

Copyright © 2026 Mateo Noriega.
