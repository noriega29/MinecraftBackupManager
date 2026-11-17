namespace MinecraftBackupManager.Restic.Models
{
    public sealed record ResticBackupResult
    (
        string SnapshotId,
        bool IsIncomplete,
        long FilesNew,
        long FilesChanged,
        long FilesUnmodified,
        long DataAdded,
        TimeSpan Duration,
        IReadOnlyList<ResticBackupWarning> Warnings
    );
}
