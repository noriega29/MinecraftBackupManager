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

        // Si se asigna, GetRepositoryStateAsync falla con esta excepción.
        public Exception? StateException { get; set; }

        // Si se asigna, BackupAsync falla con esta excepción
        // (después de esperar la compuerta, si hay una).
        public Exception? BackupException { get; set; }

        // Lo que devolverá BackupAsync.
        public ResticBackupResult BackupResult { get; set; } = CreateCompleteResult();

        // Si se asigna, BackupAsync no termina hasta que este Task termine
        // (o hasta que se cancele). Sirve para simular un respaldo lento.
        public Task? BackupGate { get; set; }

        // Se completa en cuanto BackupAsync empieza a ejecutarse.
        public TaskCompletionSource BackupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? LastBackupSourcePath { get; private set; }

        public ResticBackupKind? LastBackupKind { get; private set; }

        // Si se asigna, ForgetAsync falla con esta excepción.
        public Exception? ForgetException { get; set; }

        // Si se asigna, PruneAsync falla con esta excepción.
        public Exception? PruneException { get; set; }

        // Igual que BackupGate, pero para forget y prune.
        public Task? ForgetGate { get; set; }

        public Task? PruneGate { get; set; }

        // Se completan en cuanto ForgetAsync o PruneAsync empiezan a ejecutarse.
        public TaskCompletionSource ForgetStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PruneStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ResticRepositoryState> GetRepositoryStateAsync(CancellationToken cancellationToken = default)
        {
            _log.Add("repository-state");

            if (StateException is not null)
            {
                return Task.FromException<ResticRepositoryState>(StateException);
            }

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

            if (BackupException is not null)
            {
                throw BackupException;
            }

            return BackupResult;
        }

        public Task<IReadOnlyList<ResticSnapshot>> ListSnapshotsAsync(ResticBackupKind? kind = null, CancellationToken cancellationToken = default)
        {
            _log.Add("list-snapshots");

            return Task.FromResult<IReadOnlyList<ResticSnapshot>>(Array.Empty<ResticSnapshot>());
        }

        public async Task ForgetAsync(ResticBackupKind kind, int keepLast, CancellationToken cancellationToken = default)
        {
            _log.Add($"forget:{kind}:{keepLast}");

            ForgetStarted.TrySetResult();

            if (ForgetGate is not null)
            {
                await ForgetGate.WaitAsync(cancellationToken);
            }

            if (ForgetException is not null)
            {
                throw ForgetException;
            }
        }

        public async Task PruneAsync(CancellationToken cancellationToken = default)
        {
            _log.Add("prune");

            PruneStarted.TrySetResult();

            if (PruneGate is not null)
            {
                await PruneGate.WaitAsync(cancellationToken);
            }

            if (PruneException is not null)
            {
                throw PruneException;
            }
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
