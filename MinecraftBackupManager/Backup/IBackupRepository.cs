using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Operaciones del repositorio de copias que necesita un respaldo.
    /// </summary>
    public interface IBackupRepository
    {
        Task<ResticRepositoryState> GetRepositoryStateAsync(CancellationToken cancellationToken = default);

        Task<ResticBackupResult> BackupAsync(string sourcePath, ResticBackupKind kind, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ResticSnapshot>> ListSnapshotsAsync(ResticBackupKind? kind = null, CancellationToken cancellationToken = default);

        Task ForgetAsync(ResticBackupKind kind, int keepLast, CancellationToken cancellationToken = default);

        Task PruneAsync(CancellationToken cancellationToken = default);
    }
}
