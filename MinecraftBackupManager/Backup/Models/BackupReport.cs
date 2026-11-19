using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Backup.Models
{
    public sealed record BackupReport(
        BackupStatus Status,
        string Message,
        ResticBackupResult? ResticResult,
        ResticBackupKind Kind,
        bool AutosaveRestored
    );
}
