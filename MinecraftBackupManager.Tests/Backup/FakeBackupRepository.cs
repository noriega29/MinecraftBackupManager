using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Backup
{
    internal sealed class FakeBackupRepository : IBackupRepository
    {
        private readonly BackupCallLog _log;

        public FakeBackupRepository(BackupCallLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // Lo que devolverá GetRepositoryStateAsync.
        public ResticRepositoryState State { get; set; } = ResticRepositoryState.Ready;

        // Lo que devolverá BackupAsync.
        public ResticBackupResult BackupResult { get; set; } = CreateCompleteResult();

        // Si se asigna, BackupAsync no termina hasta que este Task termine
        // (o hasta que se cancele). Sirve para simular un respaldo lento.
        public Task? BackupGate { get; set; }

        // Se completa en cuanto BackupAsync empieza a ejecutarse.
        public TaskCompletionSource BackupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? LastBackupSourcePath { get; private set; }

        public ResticBackupKind? LastBackupKind { get; private set; }

        public Task<ResticRepositoryState> GetRepositoryStateAsync(CancellationToken cancellationToken = default)
        {
            _log.Add("repository-state");

            return Task.FromResult(State);
        }

        public async Task<ResticBackupResult> BackupAsync(string sourcePath, ResticBackupKind kind, CancellationToken cancellationToken = default)
        {
            _log.Add("backup");

            LastBackupSourcePath = sourcePath;
            LastBackupKind = kind;

            BackupStarted.TrySetResult();

            if (BackupGate is not null)
            {
                await BackupGate.WaitAsync(cancellationToken);
            }

            return BackupResult;
        }

        public Task<IReadOnlyList<ResticSnapshot>> ListSnapshotsAsync(ResticBackupKind? kind = null, CancellationToken cancellationToken = default)
        {
            _log.Add("list-snapshots");

            return Task.FromResult<IReadOnlyList<ResticSnapshot>>(Array.Empty<ResticSnapshot>());
        }

        public Task ForgetAsync(ResticBackupKind kind, int keepLast, CancellationToken cancellationToken = default)
        {
            _log.Add($"forget:{kind}:{keepLast}");

            return Task.CompletedTask;
        }

        public Task PruneAsync(CancellationToken cancellationToken = default)
        {
            _log.Add("prune");

            return Task.CompletedTask;
        }

        public static ResticBackupResult CreateCompleteResult()
        {
            return new ResticBackupResult(
                SnapshotId: new string('a', 64),
                IsIncomplete: false,
                FilesNew: 1,
                FilesChanged: 0,
                FilesUnmodified: 0,
                DataAdded: 100,
                Duration: TimeSpan.FromSeconds(1),
                Warnings: Array.Empty<ResticBackupWarning>());
        }
    }
}
